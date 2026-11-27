# sb0t multiplataforma (.NET 9) con gestor web de salas

Port del servidor de chat [AresChat/sb0t](https://github.com/AresChat/sb0t) a .NET 9 para que corra en Windows y Linux sin la interfaz gráfica de Windows. Incluye una API de administración por sala y un gestor web que crea una sala por contenedor Docker.

Licencia: AGPL-3.0, igual que el proyecto original. Si ofreces una sala al público con una versión modificada, enlaza a este repositorio en el mensaje de bienvenida.

## Qué hay aquí

| Carpeta | Para qué sirve |
|---|---|
| `core`, `commands`, `scripting`, `iconnect`, `captcha` | El servidor, migrado a .NET 9 (SQLite, imágenes con SkiaSharp, ajustes en JSON) |
| `jurassic` | Submódulo: motor de JavaScript de los scripts (rama `net9-port` de [dev45/jurassic](https://github.com/dev45/jurassic)) |
| `roomhost` | Programa de consola que arranca una sala (sustituye a la antigua ventana) |
| `manager` | Gestor web: crea, inicia, detiene y administra salas |
| `deploy` | `docker-compose.yml` y Caddyfile para publicar el gestor con HTTPS |
| `gui` | La interfaz original de Windows (no se compila ni se usa) |

## Probar una sala en tu PC

Necesitas el SDK de .NET 9. Clona con submódulos:

```
git clone --recurse-submodules -b migracion-net https://github.com/dev45/sb0t.git
cd sb0t
```

```powershell
$env:ROOM_DATA  = "C:\salas\mi-sala"
$env:ROOM_PORT  = "5009"
$env:ROOM_NAME  = "Mi sala"
$env:ROOM_OWNER = "una-clave-larga-tuya"
dotnet run --project roomhost
```

Para entrar como dueño escribe `/login TU_CLAVE` en el chat. Comprueba las piezas nativas con `dotnet run --project roomhost -- --selftest`.

### Variables de la sala

| Variable | Efecto |
|---|---|
| `ROOM_DATA` | Carpeta de datos (por defecto `data` junto al ejecutable) |
| `ROOM_PORT`, `ROOM_NAME`, `ROOM_BOT`, `ROOM_TOPIC` | Ajustes básicos |
| `ROOM_OWNER` | Contraseña de dueño. Si falta, se genera una y se muestra solo en la consola |
| `ROOM_ADMIN_TOKEN` | Activa la API de administración (24+ caracteres). Sin ella, la API no arranca |
| `ROOM_ADMIN_URL` | Dirección de la API (por defecto `http://127.0.0.1:8081`) |
| `ROOM_PUBLIC_HOST`, `ROOM_PUBLIC_PORT` | Imprime los enlaces `arlnk://` y `cb0t://` con esa dirección |
| `ROOM_EXTENSIONS` | Extensiones a cargar, separadas por comas |

## Con Docker
docker run -d --name sala --restart unless-stopped
-p 5009:5009/tcp -p 5009:5009/udp
-e ROOM_NAME="Mi sala" -e ROOM_OWNER="una-clave-larga"
-v sala-datos:/data ghcr.io/dev45/areschat-room:latest

## Gestor web

El gestor controla Docker, así que quien entre al panel administra el servidor entero. Publícalo solo detrás de HTTPS.

cd deploy
cp .env.example .env # pon MANAGER_DOMAIN y una MANAGER_ADMIN_PASSWORD larga
docker compose up -d


Abre los puertos 80 y 443, y 5009-5100 (TCP y UDP) en el firewall del sistema y en el del proveedor. Para desarrollar sin Docker, usa `MANAGER_RUNTIME=fake` (simula las salas) o `MANAGER_RUNTIME=local` (administra una sala que ya corre en tu PC).

## Limitaciones conocidas

- Con CGNAT no se puede alojar una sala en casa: hace falta IP pública o un servidor.
- Los ajustes cambiados por la API se aplican al reiniciar la sala.
- Las contraseñas de dueño y los tokens por sala se guardan en texto plano en `manager.db`.
- Las extensiones C# antiguas (compiladas contra .NET Framework) probablemente no cargan; los scripts de JavaScript sí.
- Falta probar con clientes reales en Linux: UDP y lista de salas, scripts y avatares.
- cb0t 3.36 se queda congelado al entrar en una sala (también en otra sala pública); el cliente Ares funciona.