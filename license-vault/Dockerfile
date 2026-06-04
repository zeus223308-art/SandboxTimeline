FROM node:20-alpine

WORKDIR /app

COPY package.json package-lock.json* ./
RUN npm ci --omit=dev

COPY src ./src

ENV NODE_ENV=production
ENV PORT=8787
ENV DATABASE_PATH=/data/licenses.db

RUN mkdir -p /data
VOLUME ["/data"]

EXPOSE 8787

CMD ["node", "src/server.js"]
