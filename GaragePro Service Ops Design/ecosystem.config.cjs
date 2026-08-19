module.exports = {
  apps: [
    {
      name: "garagepro-service-ops",
      script: "npm",
      args: "start",
      cwd: __dirname,
      env: {
        NODE_ENV: "production",
        HOST: "127.0.0.1",
        PORT: process.env.PORT || "3040"
      },
      autorestart: true,
      max_memory_restart: "256M",
      time: true
    }
  ]
};
