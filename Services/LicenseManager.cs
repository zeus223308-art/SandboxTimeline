using System.Net.Http;

using System.Text.Json;



namespace SandboxTimeline;



public sealed class LicenseManager : IDisposable

{

    private readonly HttpClient _httpClient;

    private readonly LicenseVaultClient _vaultClient;

    private readonly LicenseTokenStore _tokenStore = new();

    private LicenseState _cachedState = LicenseState.Free();

    private StoredLicenseToken? _storedToken;

    private string? _lastStartupDiagnostic;



    public LicenseManager(string? vaultEndpointUrl = null)

    {

#if DEBUG

        WebhookUrl = vaultEndpointUrl ?? ProductionLicenseConfiguration.DefaultProductionVaultEndpointUrl;

#else

        WebhookUrl = ProductionLicenseConfiguration.ResolveVaultEndpointUrl(vaultEndpointUrl);

#endif

        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };

        _vaultClient = new LicenseVaultClient(_httpClient, WebhookUrl);



#if DEBUG

        if (DeveloperLicenseResetPolicy.ForceClearStoredLicenseOnStartup)

        {

            ForceClearLocalLicenseState();

        }

        else

#endif

        {

            try

            {

                RestorePremiumFromEncryptedStore();

            }

            catch (Exception ex)

            {

                _lastStartupDiagnostic = ExceptionDisplayFormatter.Format(ex);

                _cachedState = LicenseState.Free();

                _storedToken = null;

            }

        }

    }



    public string WebhookUrl { get; }



    public bool IsPremium => _cachedState.IsPremium;



    public DateTime? PremiumExpiresAtUtc => _cachedState.ExpiresAtUtc;



    public string? LastStartupDiagnostic => _lastStartupDiagnostic;



    public event EventHandler? LicenseChanged;



    public int GetMaxFreeSnapshots() => IsPremium ? 50 : 3;



    public bool CanUseAutoSandbox() => IsPremium || _cachedState.TrialSandboxEnabled;



    public void ForceClearLocalLicenseState()

    {

        _tokenStore.Clear();

        _storedToken = null;

        _cachedState = LicenseState.Free();

        _lastStartupDiagnostic = null;

        LicenseChanged?.Invoke(this, EventArgs.Empty);

    }



    public void RestorePremiumFromEncryptedStore()

    {

        if (!_tokenStore.TryLoad(out var token) || token == null)

        {

            return;

        }



        token = MigrateStoredTokenIfNeeded(token);

        _storedToken = token;



        if (!token.IsPremium)

        {

            return;

        }



        if (token.ExpiresAtUtc <= DateTime.UtcNow)

        {

            ClearStoredPremiumState();

            _lastStartupDiagnostic = Loc.Get("Str_LicenseSubscriptionRevoked");

            return;

        }



        if (OfflineGracePolicy.IsGraceExpired(token.OfflineGraceExpiresAtUtc))

        {

            ClearStoredPremiumState();

            _lastStartupDiagnostic = Loc.Get("Str_LicenseOfflineGraceExpired");

            return;

        }



#if !DEBUG

        if (IsDebugOnlyStoredToken(token))

        {

            ClearStoredPremiumState();

            return;

        }

#endif



        _cachedState = new LicenseState(

            IsValid: true,

            IsPremium: true,

            TrialSandboxEnabled: false,

            ExpiresAtUtc: token.ExpiresAtUtc,

            SubscriptionId: token.SubscriptionId,

            CustomerId: token.CustomerId);

        LicenseChanged?.Invoke(this, EventArgs.Empty);

    }



    public async Task<LicenseActivationResult> ActivateLicenseAsync(

        string licenseKey,

        CancellationToken cancellationToken = default)

    {

        if (string.IsNullOrWhiteSpace(licenseKey))

        {

            return LicenseActivationResult.Failed(Loc.Get("Str_LicenseValidationFailedSoft"));

        }



        var trimmedKey = licenseKey.Trim();



        try

        {

#if DEBUG

            if (DebugLicenseAuthorization.TryCreatePremiumState(trimmedKey, out var debugState))

            {

                return await CommitPremiumActivationAsync(trimmedKey, debugState, cancellationToken)

                    .ConfigureAwait(false);

            }

#endif



            var state = await ValidateViaLicenseVaultAsync(trimmedKey, cancellationToken)

                .ConfigureAwait(false);

            if (!state.IsValid || !state.IsPremium)

            {

                return LicenseActivationResult.Failed(

                    Loc.Get("Str_LicenseValidationFailedSoft"),

                    isNetworkError: state.LastErrorWasNetwork,

                    diagnosticDetails: state.DiagnosticDetails);

            }



            return await CommitPremiumActivationAsync(trimmedKey, state, cancellationToken)

                .ConfigureAwait(false);

        }

        catch (HttpRequestException ex)

        {

            return LicenseActivationResult.Failed(

                Loc.Get("Str_LicenseValidationFailedSoft"),

                isNetworkError: true,

                diagnosticDetails: ExceptionDisplayFormatter.Format(ex));

        }

        catch (TaskCanceledException ex)

        {

            return LicenseActivationResult.Failed(

                Loc.Get("Str_LicenseValidationFailedSoft"),

                isNetworkError: true,

                diagnosticDetails: ExceptionDisplayFormatter.Format(ex));

        }

        catch (Exception ex)

        {

            return LicenseActivationResult.Failed(

                Loc.Get("Str_LicenseValidationFailedSoft"),

                diagnosticDetails: ExceptionDisplayFormatter.Format(ex));

        }

    }



    public async Task SyncSubscriptionEntitlementAsync(CancellationToken cancellationToken = default)

    {

        RestorePremiumFromEncryptedStore();



        if (_storedToken == null || !_storedToken.IsPremium)

        {

            return;

        }



#if DEBUG

        if (DebugLicenseAuthorization.IsDebugStoredToken(_storedToken))

        {

            return;

        }

#endif



        if (OfflineGracePolicy.IsGraceExpired(_storedToken.OfflineGraceExpiresAtUtc))

        {

            RevokePremiumEntitlement(Loc.Get("Str_LicenseOfflineGraceExpired"));

            return;

        }



        var entitlement = await _vaultClient

            .ValidateStoredTokenAsync(_storedToken, cancellationToken)

            .ConfigureAwait(false);



        if (entitlement.IsEntitled)

        {

            ApplyVaultEntitlementState(entitlement);

            _lastStartupDiagnostic = null;

            LicenseChanged?.Invoke(this, EventArgs.Empty);

            return;

        }



        if (entitlement.IsNetworkError)

        {

            ApplyOfflineGraceFallback(entitlement.Message);

            return;

        }



        RevokePremiumEntitlement(entitlement.Message ?? Loc.Get("Str_LicenseSubscriptionRevoked"));

    }



    public async Task RefreshLicenseAsync(CancellationToken cancellationToken = default)

    {

        try

        {

            RestorePremiumFromEncryptedStore();



            if (_storedToken == null || string.IsNullOrWhiteSpace(_storedToken.LicenseKeyHash))

            {

                if (!IsPremium)

                {

                    _cachedState = LicenseState.Free();

                }



                return;

            }



            if (_storedToken.ExpiresAtUtc <= DateTime.UtcNow)

            {

                ClearStoredPremiumState();

                LicenseChanged?.Invoke(this, EventArgs.Empty);

                return;

            }



            if (OfflineGracePolicy.IsGraceExpired(_storedToken.OfflineGraceExpiresAtUtc))

            {

                RevokePremiumEntitlement(Loc.Get("Str_LicenseOfflineGraceExpired"));

                return;

            }



#if DEBUG

            if (DebugLicenseAuthorization.IsDebugStoredToken(_storedToken))

            {

                _cachedState = new LicenseState(

                    true,

                    true,

                    false,

                    _storedToken.ExpiresAtUtc,

                    _storedToken.SubscriptionId,

                    _storedToken.CustomerId);

                LicenseChanged?.Invoke(this, EventArgs.Empty);

                return;

            }

#endif



            var state = await ValidateStoredTokenViaVaultAsync(cancellationToken).ConfigureAwait(false);

            if (state.IsValid && state.IsPremium)

            {

                ApplyValidatedPremiumState(state);

                _lastStartupDiagnostic = null;

            }

            else if (state.LastErrorWasNetwork)

            {

                ApplyOfflineGraceFallback(state.DiagnosticDetails);

            }

            else

            {

                RevokePremiumEntitlement(state.DiagnosticDetails ?? Loc.Get("Str_LicenseSubscriptionRevoked"));

            }

        }

        catch (Exception ex)

        {

            _lastStartupDiagnostic = ExceptionDisplayFormatter.Format(ex);

            ApplyOfflineGraceFallback(_lastStartupDiagnostic);

        }

        finally

        {

            LicenseChanged?.Invoke(this, EventArgs.Empty);

        }

    }



    private void ApplyOfflineGraceFallback(string? diagnosticDetails)

    {

        _lastStartupDiagnostic = diagnosticDetails;



        if (_storedToken == null)

        {

            _cachedState = LicenseState.Free();

            return;

        }



        if (OfflineGracePolicy.IsGraceExpired(_storedToken.OfflineGraceExpiresAtUtc))

        {

            RevokePremiumEntitlement(Loc.Get("Str_LicenseOfflineGraceExpired"));

            return;

        }



        if (_storedToken.ExpiresAtUtc > DateTime.UtcNow)

        {

            _cachedState = new LicenseState(

                IsValid: true,

                IsPremium: true,

                TrialSandboxEnabled: false,

                ExpiresAtUtc: _storedToken.ExpiresAtUtc,

                SubscriptionId: _storedToken.SubscriptionId,

                CustomerId: _storedToken.CustomerId);

            return;

        }



        ClearStoredPremiumState();

    }



    private async Task<LicenseActivationResult> CommitPremiumActivationAsync(

        string licenseKey,

        LicenseState state,

        CancellationToken cancellationToken)

    {

        var validatedAtUtc = DateTime.UtcNow;

        var expiresAt = state.ExpiresAtUtc ?? validatedAtUtc.AddYears(1);

        var stored = new StoredLicenseToken

        {

            ActivationToken = Guid.NewGuid().ToString("N"),

            ExpiresAtUtc = expiresAt,

            IsPremium = true,

            LicenseKey = string.Empty,

            LicenseKeyHash = LicenseKeyHasher.ComputeHash(licenseKey),

            ProductMetadataId = ProductionLicenseConfiguration.ProductMetadataId,

            LastOnlineValidationUtc = validatedAtUtc,

            OfflineGraceExpiresAtUtc = OfflineGracePolicy.ComputeGraceExpiryUtc(validatedAtUtc),

            SubscriptionId = state.SubscriptionId,

            CustomerId = state.CustomerId,

            ActivatedAtUtc = validatedAtUtc

        };



        await Task.Run(() => _tokenStore.Save(stored), cancellationToken).ConfigureAwait(false);

        _storedToken = stored;

        _cachedState = state;

        _lastStartupDiagnostic = null;

        LicenseChanged?.Invoke(this, EventArgs.Empty);

        return LicenseActivationResult.Succeeded(Loc.Get("Str_PremiumActivatedThanks"));

    }



    private async Task<LicenseState> ValidateViaLicenseVaultAsync(

        string licenseKey,

        CancellationToken cancellationToken)

    {

        if (!_vaultClient.IsConfigured)

        {

            return LicenseState.Free() with

            {

                DiagnosticDetails = "License vault endpoint URL is not configured."

            };

        }



        var vaultResult = await _vaultClient.ValidateLicenseKeyAsync(licenseKey, cancellationToken)

            .ConfigureAwait(false);

        return MapVaultResultToLicenseState(vaultResult);

    }



    private async Task<LicenseState> ValidateStoredTokenViaVaultAsync(CancellationToken cancellationToken)

    {

        if (_storedToken == null)

        {

            return LicenseState.Free();

        }



        if (!_vaultClient.IsConfigured)

        {

            return LicenseState.Free() with

            {

                DiagnosticDetails = "License vault endpoint URL is not configured."

            };

        }



        var vaultResult = await _vaultClient.ValidateStoredTokenAsync(_storedToken, cancellationToken)

            .ConfigureAwait(false);

        return MapVaultResultToLicenseState(vaultResult);

    }



    private static LicenseState MapVaultResultToLicenseState(VaultLicenseValidationResult vaultResult)

    {

        if (vaultResult.IsEntitled)

        {

            return new LicenseState(

                IsValid: true,

                IsPremium: true,

                TrialSandboxEnabled: false,

                ExpiresAtUtc: vaultResult.SubscriptionExpiresAtUtc,

                SubscriptionId: vaultResult.SubscriptionId,

                CustomerId: vaultResult.CustomerId,

                LastErrorWasNetwork: false);

        }



        if (vaultResult.IsNetworkError)

        {

            return LicenseState.Free() with

            {

                LastErrorWasNetwork = true,

                DiagnosticDetails = vaultResult.Message

            };

        }



        return LicenseState.Free() with { DiagnosticDetails = vaultResult.Message };

    }



    private void ApplyValidatedPremiumState(LicenseState state)

    {

        if (_storedToken == null)

        {

            _cachedState = state;

            return;

        }



        var validatedAtUtc = DateTime.UtcNow;

        _cachedState = state;

        var updated = _storedToken with

        {

            ExpiresAtUtc = state.ExpiresAtUtc ?? _storedToken.ExpiresAtUtc,

            SubscriptionId = state.SubscriptionId ?? _storedToken.SubscriptionId,

            CustomerId = state.CustomerId ?? _storedToken.CustomerId,

            LastOnlineValidationUtc = validatedAtUtc,

            OfflineGraceExpiresAtUtc = OfflineGracePolicy.ComputeGraceExpiryUtc(validatedAtUtc),

            LicenseKey = string.Empty

        };

        _tokenStore.Save(updated);

        _storedToken = updated;

    }



    private void ApplyVaultEntitlementState(VaultLicenseValidationResult entitlement)

    {

        if (_storedToken == null)

        {

            return;

        }



        var validatedAtUtc = DateTime.UtcNow;

        var expiresAt = entitlement.SubscriptionExpiresAtUtc ?? _storedToken.ExpiresAtUtc;

        _cachedState = new LicenseState(

            IsValid: true,

            IsPremium: true,

            TrialSandboxEnabled: false,

            ExpiresAtUtc: expiresAt,

            SubscriptionId: entitlement.SubscriptionId ?? _storedToken.SubscriptionId,

            CustomerId: entitlement.CustomerId ?? _storedToken.CustomerId);



        var updated = _storedToken with

        {

            ExpiresAtUtc = expiresAt,

            SubscriptionId = entitlement.SubscriptionId ?? _storedToken.SubscriptionId,

            CustomerId = entitlement.CustomerId ?? _storedToken.CustomerId,

            ProductMetadataId = entitlement.ProductMetadataId ?? _storedToken.ProductMetadataId,

            LastOnlineValidationUtc = validatedAtUtc,

            OfflineGraceExpiresAtUtc = OfflineGracePolicy.ComputeGraceExpiryUtc(validatedAtUtc),

            LicenseKey = string.Empty

        };

        _tokenStore.Save(updated);

        _storedToken = updated;

    }



    private void RevokePremiumEntitlement(string? reason)

    {

        ClearStoredPremiumState();

        _lastStartupDiagnostic = reason;

        LicenseChanged?.Invoke(this, EventArgs.Empty);

    }



    private void ClearStoredPremiumState()

    {

        _tokenStore.Clear();

        _storedToken = null;

        _cachedState = LicenseState.Free();

    }



    private StoredLicenseToken MigrateStoredTokenIfNeeded(StoredLicenseToken token)

    {

        var migrated = token;

        var changed = false;



        if (string.IsNullOrWhiteSpace(token.LicenseKeyHash) && !string.IsNullOrWhiteSpace(token.LicenseKey))

        {

            migrated = migrated with

            {

                LicenseKeyHash = LicenseKeyHasher.ComputeHash(token.LicenseKey),

                LicenseKey = string.Empty

            };

            changed = true;

        }



        if (token.LastOnlineValidationUtc == default)

        {

            var fallbackValidationUtc = token.ActivatedAtUtc == default

                ? DateTime.UtcNow

                : token.ActivatedAtUtc;

            migrated = migrated with

            {

                LastOnlineValidationUtc = fallbackValidationUtc,

                OfflineGraceExpiresAtUtc = OfflineGracePolicy.ComputeGraceExpiryUtc(fallbackValidationUtc)

            };

            changed = true;

        }



        if (string.IsNullOrWhiteSpace(token.ProductMetadataId))

        {

            migrated = migrated with

            {

                ProductMetadataId = ProductionLicenseConfiguration.ProductMetadataId

            };

            changed = true;

        }



        if (changed)

        {

            _tokenStore.Save(migrated);

        }



        return migrated;

    }



#if !DEBUG

    private static bool IsDebugOnlyStoredToken(StoredLicenseToken token) =>

        string.Equals(token.SubscriptionId, "sub_debug_local_only", StringComparison.Ordinal);

#endif



    public void Dispose()

    {

        _vaultClient.Dispose();

        _httpClient.Dispose();

    }



    public record struct LicenseState(

        bool IsValid,

        bool IsPremium,

        bool TrialSandboxEnabled,

        DateTime? ExpiresAtUtc,

        string? SubscriptionId = null,

        string? CustomerId = null,

        bool LastErrorWasNetwork = false,

        string? DiagnosticDetails = null)

    {

        public static LicenseState Free() => new(false, false, false, null);

    }

}


