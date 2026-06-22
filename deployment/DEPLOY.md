# Deploying Nook to nook.alibalib.com

Nook joins the **alibalib.com** multi-app platform (DigitalOcean droplet
`162.243.174.107`, nginx + Docker Compose). It shares the existing
**`awblazor-sqlserver`** container (new database `Nook`) and runs as its own app
container on **`127.0.0.1:8083`**. Nginx reverse-proxies the subdomain and
terminates TLS. Nook is **public** (its own ASP.NET Identity login).

Run the steps in order. `$` = on the droplet over SSH (use PuTTY — the DO web
console mangles long pasted lines).

---

## 1. DNS at Porkbun

Porkbun → **Domain Management → alibalib.com → DNS → Edit**. Add an **A** record (TTL 600):

| Type | Host | Answer |
|---|---|---|
| A | `nook` | `162.243.174.107` |

Verify before continuing (must return the IP):
```bash
$ dig +short nook.alibalib.com
```

## 2. Push the image to GHCR (from your machine)

Commit + push to `main`. GitHub Actions builds and publishes
`ghcr.io/capnbigal/nook:latest`. Then make it pullable by the droplet:
- GitHub → profile → **Packages → nook → Package settings → Change visibility → Public**, **or**
- keep it private and `docker login ghcr.io` on the droplet with a PAT.

## 3. Put the deploy files on the droplet

```bash
$ sudo git clone https://github.com/capnbigal/Nook.git /opt/nook
$ cd /opt/nook
$ cp deployment/.env.template .env && sudo chmod 600 .env
$ sudo nano .env     # set SA_PASSWORD to the SAME value as /opt/awblazor/.env
$ grep SA_PASSWORD /opt/awblazor/.env /opt/nook/.env   # confirm they match
```

## 4. Start the container

```bash
$ cd /opt/nook
$ sudo docker compose pull app
$ sudo docker compose up -d app
$ sudo docker compose logs -f app   # watch for: Now listening on: http://[::]:8080
```

On first start it creates the `Nook` database on the shared SQL Server, applies
migrations, and seeds a demo user (`demo@nook.local` / `Demo123!`) + sample items.
Smoke test on the box:
```bash
$ curl -sI http://127.0.0.1:8083 | head -1     # expect HTTP/1.1 200
```

## 5. Nginx site

```bash
$ sudo cp /opt/nook/deployment/nginx-nook.conf /etc/nginx/sites-available/nook.conf
$ sudo ln -s /etc/nginx/sites-available/nook.conf /etc/nginx/sites-enabled/
$ sudo nginx -t && sudo systemctl reload nginx
```

## 6. TLS

```bash
$ sudo certbot --nginx -d nook.alibalib.com
$ sudo nginx -t && sudo systemctl reload nginx
```

## 7. Verify

```bash
$ curl -sI https://nook.alibalib.com | head -1   # 200
```
Open it in a browser, log in as the demo user, and confirm the interactive
circuit connects (no persistent "reconnecting" overlay — that means the
WebSocket proxy works).

---

## Routine updates later

Push to `main` → Actions rebuilds the image → on the droplet:
```bash
$ cd /opt/nook && sudo docker compose pull app && sudo docker compose up -d app
```
Rollback: `APP_TAG=<short-sha> sudo docker compose up -d app`.

## Notes
- **Connection string** is injected by compose (env), overriding `appsettings*.json`.
- **Backups:** AWBlazor's instance-wide SQL backup includes the new `Nook` DB automatically.
- The demo account has a known password — it exists for showcase purposes. Change
  or remove the seed in `Data/DbSeeder.cs` if you don't want it on a public site.
