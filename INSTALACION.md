# Instalación paso a paso (desde cero)

Guía para instalar y ejecutar el POS en una computadora nueva con **Windows 10/11**.
Si ya lo tienes instalado, ve directo a [Arrancar cada vez](#5-arrancar-cada-vez).

```
Navegador (Blazor)  ──►  API .NET 10 (puerto 5100)  ──►  Odoo 20 + PostgreSQL en Docker (puerto 8069)
```

Todo puede correr en **una sola computadora**. Para la presentación con dos computadoras
mira la [sección 7](#7-presentación-con-dos-computadoras).

---

## 1. Instalar los programas (una sola vez)

| Programa | Para qué | Descarga |
|---|---|---|
| **Git** | Descargar el repositorio | <https://git-scm.com/download/win> (dejar las opciones por defecto) |
| **.NET SDK 10** | Compilar y ejecutar la API y el frontend | <https://dotnet.microsoft.com/download/dotnet/10.0> → *SDK*, Windows x64 |
| **Docker Desktop** | Ejecutar Odoo y PostgreSQL | <https://www.docker.com/products/docker-desktop/> |

Notas sobre Docker Desktop:
- Durante la instalación deja marcada la opción **WSL 2**. Si al abrirlo pide actualizar WSL,
  abre PowerShell y ejecuta `wsl --update`, luego reinicia la computadora.
- Ábrelo y espera a que abajo a la izquierda diga **Engine running** (ballena verde).
- Necesita unos **4 GB de RAM libres** y ~3 GB de disco para las imágenes de Odoo y PostgreSQL.

Comprueba que todo quedó instalado. Abre **PowerShell** (tecla Windows → escribe *PowerShell*) y ejecuta:

```powershell
git --version        # git version 2.x
dotnet --version     # 10.x.x
docker --version     # Docker version 2x.x
```

Si alguno dice "no se reconoce como comando", cierra PowerShell, vuelve a abrirlo
(o reinicia la computadora) y repite.

## 2. Permitir los scripts de PowerShell (una sola vez)

Windows bloquea los scripts `.ps1` por defecto. En PowerShell:

```powershell
Set-ExecutionPolicy -Scope CurrentUser RemoteSigned
```

Responde **S** (o **Y**) si pregunta.

## 3. Descargar el proyecto (una sola vez)

Usa una carpeta **sin espacios y fuera de OneDrive** (OneDrive bloquea archivos mientras se
compila). Por ejemplo `C:\dev`:

```powershell
mkdir C:\dev -Force
cd C:\dev
git clone https://github.com/Sandrofer/Proyecto_PATRONES.git
cd Proyecto_PATRONES
```

> Si lo descargaste como **ZIP** en lugar de `git clone`, después de descomprimirlo ejecuta
> dentro de la carpeta: `Get-ChildItem -Recurse | Unblock-File`

## 4. Preparar Odoo (una sola vez, ~5 minutos)

Con **Docker Desktop abierto**, en la carpeta del proyecto:

```powershell
.\scripts\odoo-setup.ps1
```

La primera vez descarga las imágenes de Odoo y PostgreSQL y crea la base de datos; puede tardar
varios minutos según el internet. Al terminar muestra **"Odoo listo"** en verde.

El script deja configurado, sin datos tuyos ni contraseñas en el repositorio:
- Odoo 20 en español con la base `pos`, IVA 15 %, productos con stock, pago en **Efectivo**,
  el punto de venta **Cafetería - Caja 1** y una **sesión de caja abierta**.
- Un usuario de integración `pos_integration` con su **API key**, guardada en
  `dotnet user-secrets` de **tu** computadora (cada uno tiene la suya; nunca se sube a GitHub).
- Una clave aleatoria para los tokens de inicio de sesión.

Se puede ejecutar de nuevo sin problema: no duplica nada y, si la caja se cerró, abre una nueva.

### 4.1 Crear un cajero en Odoo

Los cajeros del POS son usuarios de Odoo:

1. Abre <http://localhost:8069> e inicia sesión con usuario **`admin`** y contraseña **`admin`**.
2. Ve a **Ajustes → Usuarios y empresas → Usuarios → Nuevo**.
3. Llena **Nombre** (ej. `Cajero 1`) y **Correo electrónico / usuario** (ej. `cajero1`).
4. En la pestaña **Permisos de acceso**, en **Punto de venta**, elige **Usuario**.
5. Guarda con el ícono de la **nube ☁** (arriba).
6. Pulsa el engranaje **⚙ / ⋮ → Cambiar contraseña**, escribe una contraseña
   (ej. `Cajero2026!`) y confirma.

> Recomendado: cambia también la contraseña de `admin` en Odoo (menú de tu usuario, arriba a la
> derecha → *Mi perfil* → *Seguridad*), sobre todo si vas a conectarte a una red compartida.

## 5. Arrancar (cada vez)

1. Abre **Docker Desktop** y espera a *Engine running*.
2. **Ventana de PowerShell 1 — backend** (deja esta ventana abierta):
   ```powershell
   cd C:\dev\Proyecto_PATRONES
   .\scripts\backend-start.ps1
   ```
   Listo cuando aparece `Now listening on: http://0.0.0.0:5100`.
3. **Ventana de PowerShell 2 — frontend** (déjala abierta también):
   ```powershell
   cd C:\dev\Proyecto_PATRONES
   .\scripts\frontend-start.ps1 -ApiUrl http://localhost:5100
   ```
   Listo cuando aparece `Now listening on: http://0.0.0.0:5200`.
4. Abre <http://localhost:5200> e inicia sesión con el cajero que creaste (paso 4.1).
   Arriba a la derecha debe verse **● Odoo conectado** en verde.

La primera vez cada script compila el proyecto y tarda un poco más.

**Apagar:** `Ctrl + C` en las dos ventanas y, si quieres apagar también Odoo:

```powershell
docker compose -f docker\docker-compose.yml down
```

(Los datos de Odoo se conservan; la próxima vez basta con el paso 5.)

## 6. Comprobar que funciona

```powershell
cd C:\dev\Proyecto_PATRONES\backend;  dotnet test tests\Pos.UnitTests
cd C:\dev\Proyecto_PATRONES\frontend; dotnet test tests\Pos.Frontend.UnitTests
```

Ambas deben terminar en **Correctas!** con 0 errores. La guía completa de pruebas manuales
(descuentos, cédula, vuelto, Odoo caído, impresión) está en el [README](README.md).

## 7. Presentación con dos computadoras

> Tutorial completo, con pruebas de conexión, qué demostrar y solución de problemas:
> [docs/conexion-dos-computadoras.md](docs/conexion-dos-computadoras.md).

Las dos deben estar en la **misma red** (misma Wi-Fi). Haz los pasos 1–3 en ambas; el paso 4
solo en la del backend.

**Computadora BACKEND** (Odoo + API):
1. Una sola vez, en PowerShell **como administrador** (clic derecho → *Ejecutar como administrador*),
   abre el puerto de la API en el firewall:
   ```powershell
   New-NetFirewallRule -DisplayName "POS API 5100" -Direction Inbound -Protocol TCP -LocalPort 5100 -Action Allow -Profile Private,Domain
   ```
2. Ejecuta `.\scripts\backend-start.ps1`. Al inicio muestra la línea exacta que debe usar la otra
   computadora, por ejemplo `scripts\frontend-start.ps1 -ApiUrl http://192.168.1.20:5100`.

**Computadora FRONTEND**:
```powershell
.\scripts\frontend-start.ps1 -ApiUrl http://192.168.1.20:5100
```
(con la IP que mostró el backend). Si dice *"API alcanzable"* en verde, abre
<http://localhost:5200>. Odoo se administra desde `http://IP-DEL-BACKEND:8069`.

> En Windows la red Wi-Fi debe estar como **Privada** (Configuración → Red e Internet → Wi-Fi →
> propiedades de la red). En redes públicas o de universidad que aíslan los equipos, usa el
> punto de acceso del celular de uno de ustedes.

## 8. Problemas frecuentes

| Síntoma | Solución |
|---|---|
| `no se puede cargar el archivo ... .ps1 porque la ejecución de scripts está deshabilitada` | Paso 2 (`Set-ExecutionPolicy`). |
| `docker compose falló` / `Docker no respondió` | Abre Docker Desktop y espera a *Engine running*. |
| `failed to register layer ... input/output error` al descargar Odoo | Docker no pudo escribir la imagen en su disco (no es error del proyecto). En orden: 1) libera espacio en C: (≥ 10 GB); 2) cierra Docker Desktop, ejecuta `wsl --shutdown`, ábrelo y repite `odoo-setup.ps1` (el script ya reintenta 3 veces); 3) Docker Desktop → ícono de insecto *Troubleshoot* → *Clean / Purge data* y repite; 4) excluye Docker del antivirus durante la descarga. |
| `port is already allocated` (8069 o 5432) | Otro Odoo/PostgreSQL usa ese puerto: ciérralo o detén ese contenedor en Docker Desktop. |
| `Failed to bind to address ... 5100` o `5200` | Ya hay una API o frontend abierto: cierra la otra ventana (o reinicia la PC). |
| El POS dice **"Odoo sin conexión"** | Odoo está arrancando (espera ~30 s) o Docker está cerrado. |
| El POS dice **"Caja cerrada"** | La sesión de caja se cerró en Odoo: ejecuta otra vez `.\scripts\odoo-setup.ps1`. |
| "Usuario o contraseña incorrectos" | Usa un usuario de Odoo creado en el paso 4.1 (no `pos_integration`). |
| "no tiene permiso de Punto de venta" | En Odoo, al usuario: *Permisos de acceso → Punto de venta → Usuario*, y guarda. |
| "Ocurrió un error inesperado" en el navegador | Recarga con **Ctrl + F5**. |
| La computadora frontend no llega a la API | Misma Wi-Fi, red **Privada**, regla de firewall del paso 7 e IP correcta. |
| `dotnet` compila con errores tras actualizar | Cierra las ventanas, ejecuta `git pull` y vuelve a arrancar. |

## 9. Actualizar a la última versión

```powershell
cd C:\dev\Proyecto_PATRONES
git pull
```

Luego arranca normalmente (paso 5). No hace falta repetir el paso 4 salvo que se indique.
