const nodemailer = require("nodemailer");
const { config } = require("./config");

let transporter;

function isEmailConfigured() {
  return Boolean(config.smtp.host && config.smtp.user && config.smtp.pass);
}

function getTransporter() {
  if (!isEmailConfigured()) {
    return null;
  }

  if (!transporter) {
    transporter = nodemailer.createTransport({
      host: config.smtp.host,
      port: config.smtp.port,
      secure: config.smtp.secure,
      auth: {
        user: config.smtp.user,
        pass: config.smtp.pass
      }
    });
  }

  return transporter;
}

async function sendLicenseKeyEmail({ to, licenseKey, expiresAt }) {
  const mailer = getTransporter();
  if (!mailer || !to) {
    return { sent: false, reason: "SMTP not configured or recipient missing." };
  }

  const expiryLine = expiresAt
    ? `만료(갱신)일: ${expiresAt}`
    : "구독이 활성인 동안 사용할 수 있습니다.";

  const text = [
    "Sandbox Timeline 프리미엄 라이선스가 발급되었습니다.",
    "",
    `라이선스 키: ${licenseKey}`,
    "",
    expiryLine,
    "",
    "앱 실행 → 라이선스 키 입력란에 위 키를 붙여넣으세요.",
    "",
    "이 메일을 분실하지 마세요. 키는 결제 완료 페이지에서도 확인할 수 있습니다."
  ].join("\n");

  await mailer.sendMail({
    from: config.smtp.from,
    to,
    subject: "Sandbox Timeline — 프리미엄 라이선스 키",
    text
  });

  return { sent: true };
}

module.exports = { isEmailConfigured, sendLicenseKeyEmail };
