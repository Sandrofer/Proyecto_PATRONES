# Tutorial: conectar el POS entre dos computadoras

En la presentación el sistema corre repartido en **dos computadoras de la misma red**:

```
 COMPUTADORA 2 · FRONTEND                    COMPUTADORA 1 · BACKEND
┌──────────────────────────┐   Wi-Fi    ┌──────────────────────────────────────────┐
│ Navegador                │  ───────►  │ API .NET 10           puerto 5100        │
│ POS Blazor (puerto 5200) │  HTTP+JSON │   │  JSON-2 + API key                    │
│                          │  JWT       │   ▼                                      │
│                          │            │ Docker: Odoo 20 (8069) + PostgreSQL      │
└──────────────────────────┘            └──────────────────────────────────────────┘
```

- La computadora **frontend** solo necesita llegar al **puerto 5100** de la computadora backend.
- El frontend **nunca** habla con Odoo ni conoce la API key: eso es justamente lo que se
  demuestra (el enunciado exige *Frontend → API REST .NET → Odoo*).

---

## 1. Preparación (días antes, una sola vez)

| | Computadora 1 · BACKEND | Computadora 2 · FRONTEND |
|---|---|---|
| Git, .NET SDK 10 | Sí | Sí |
| Docker Desktop | **Sí** | No hace falta |
| Proyecto descargado (`git clone`) | Sí | Sí |
| `.\scripts\odoo-setup.ps1` | **Sí** (crea Odoo, datos y API key) | No |
| Cajero creado en Odoo | **Sí** (Ajustes → Usuarios, permiso *Punto de venta: Usuario*) | — |
| Regla de firewall del puerto 5100 | **Sí** (paso 1.1) | No |

Todo esto está detallado en [INSTALACION.md](../INSTALACION.md) (pasos 1 a 4).

### 1.1 Abrir el puerto 5100 en el BACKEND

Windows bloquea por defecto las conexiones que llegan desde otra computadora. En la computadora
backend, abre PowerShell **como administrador** (clic derecho → *Ejecutar como administrador*):

```powershell
New-NetFirewallRule -DisplayName "POS API 5100" -Direction Inbound -Protocol TCP -LocalPort 5100 -Action Allow -Profile Private,Domain
```

> `backend-start.ps1` crea esta regla solo si lo ejecutas como administrador; si no, te muestra
> este mismo comando en amarillo.

### 1.2 Marcar la red como **Privada** (en ambas computadoras)

La regla anterior solo vale en redes *Privadas* (no en *Públicas*, por seguridad).

- **Configuración → Red e Internet → Wi-Fi → (nombre de tu red) → Tipo de perfil de red → Privada.**
- O en PowerShell como administrador (cambia `Wi-Fi` si tu adaptador tiene otro nombre):
  ```powershell
  Get-NetConnectionProfile                                   # muestra el nombre y la categoría
  Set-NetConnectionProfile -InterfaceAlias "Wi-Fi" -NetworkCategory Private
  ```

---

## 2. El día de la presentación

### Paso 1 — Las dos computadoras en la misma red

Conecta ambas a la **misma Wi-Fi**. Recomendado: el **punto de acceso del celular** de un
integrante. Muchas redes de universidad tienen *aislamiento de clientes* (los equipos no se ven
entre sí) aunque estén en la misma Wi-Fi; con el hotspot eso no pasa.

### Paso 2 — Arrancar el BACKEND (computadora 1)

1. Abre **Docker Desktop** y espera a *Engine running*.
2. En PowerShell, en la carpeta del proyecto:
   ```powershell
   .\scripts\backend-start.ps1
   ```
3. Al inicio el script muestra en **verde** la línea que debe usar la otra computadora, por ejemplo:
   ```
   En la computadora FRONTEND use una de estas direcciones:
     scripts\frontend-start.ps1 -ApiUrl http://192.168.43.120:5100
   ```
   Anota esa IP. (Si aparecen varias, usa la de la Wi-Fi; la confirmas con `ipconfig` →
   *Adaptador de LAN inalámbrica Wi-Fi → Dirección IPv4*.)
4. Espera a ver `Now listening on: http://0.0.0.0:5100`. **No cierres esta ventana.**

### Paso 3 — Comprobar la conexión desde el FRONTEND (computadora 2)

Antes de arrancar nada, en PowerShell de la computadora 2 (con la IP anotada):

```powershell
Test-NetConnection 192.168.43.120 -Port 5100
```

Debe decir **`TcpTestSucceeded : True`**. También puedes abrir en el navegador
`http://192.168.43.120:5100/health` → debe mostrar `{"status":"ok"}`.

Si sale `False`, ve a [Solución de problemas](#4-solución-de-problemas) antes de seguir.

### Paso 4 — Arrancar el FRONTEND (computadora 2)

```powershell
.\scripts\frontend-start.ps1 -ApiUrl http://192.168.43.120:5100
```

- Si dice **"API alcanzable en …"** en verde, la conexión está bien.
- El script guarda esa dirección en `frontend/src/Pos.Frontend.Web/wwwroot/appsettings.json`
  (el navegador la lee al cargar; no hace falta recompilar si cambia la IP).
- Espera `Now listening on: http://0.0.0.0:5200` y abre **<http://localhost:5200>** en la
  computadora 2.
- Inicia sesión con el cajero de Odoo. Arriba a la derecha: **● Odoo conectado** (verde).

### Paso 5 — Odoo para el profesor

Odoo se administra desde cualquiera de las dos computadoras en `http://IP-DEL-BACKEND:8069`
(en la computadora 1 también `http://localhost:8069`), usuario `admin`. Ahí se muestra que la
venta hecha en la computadora 2 aparece en **Punto de venta → Órdenes → Órdenes**.

> Si `http://IP-DEL-BACKEND:8069` no abre desde la computadora 2, ábrelo en la computadora 1
> (`localhost:8069`), o agrega la regla `New-NetFirewallRule -DisplayName "Odoo 8069" -Direction Inbound -Protocol TCP -LocalPort 8069 -Action Allow -Profile Private,Domain`.
> **No** es necesario para el POS: el frontend nunca usa ese puerto.

---

## 3. Qué demostrar con las dos computadoras

1. **Separación real** — En la computadora 2 abre las herramientas del navegador (**F12 →
   Red / Network**) y haz una búsqueda o un cobro: todas las peticiones van a
   `IP-BACKEND:5100/api/...`; **ninguna** va al puerto 8069 (Odoo). La API key no aparece en
   ninguna parte del navegador.
2. **Venta de extremo a extremo** — Cobrar en la computadora 2 y mostrar la misma orden (número,
   cajero, cliente, pago y vuelto) en Odoo, en la computadora 1.
3. **Caída de Odoo** — En la computadora 1: `docker stop pos-odoo-web`. En la computadora 2
   aparece en segundos el aviso rojo y *Cobrar* se bloquea sin perder el carrito. Luego
   `docker start pos-odoo-web`: a los ~30 s el POS se recupera solo.
4. **Caída de la red** — Desconecta la Wi-Fi de la computadora 2 un momento: el indicador pasa a
   **API sin conexión**; al reconectar vuelve solo.
5. **Sin duplicados** — Doble clic rápido en *Cobrar*: una sola venta en Odoo.

---

## 4. Solución de problemas

| Síntoma | Causa probable | Solución |
|---|---|---|
| `TcpTestSucceeded : False` | Firewall del backend o red *Pública* | Pasos 1.1 y 1.2 en la computadora 1. |
| `False` aun con firewall y red privada | Wi-Fi con aislamiento de clientes | Usa el hotspot de un celular. |
| `False` y la API no está corriendo | La ventana del backend se cerró o falló | Vuelve a ejecutar `backend-start.ps1` y espera *Now listening*. |
| Funcionaba ayer y hoy no | La IP del backend cambió (DHCP) | Repite el paso 2 y usa la IP nueva en `frontend-start.ps1`. |
| "API alcanzable" pero el login falla con "No se pudo conectar…" | El navegador se abrió antes de configurar la URL | Recarga con **Ctrl + F5**. |
| **● Odoo sin conexión** en la computadora 2 | Odoo no está arriba en la computadora 1 | Docker Desktop abierto; espera ~30 s tras arrancar. |
| **Caja cerrada** | La sesión de caja se cerró en Odoo | En la computadora 1: `.\scripts\odoo-setup.ps1` (abre una nueva). |
| Antivirus de terceros (Avast, Kaspersky, ESET…) | Tienen su propio firewall | Permite el puerto 5100 en ese antivirus o desactiva su firewall durante la demo. |
| VPN activa | La VPN cambia las rutas de red | Desconecta la VPN en ambas computadoras. |
| Error `Failed to bind ... 5100` | Ya hay otra API abierta | Cierra la otra ventana o reinicia la computadora. |

### Plan B (si la red falla en la presentación)

Todo funciona también en **una sola computadora**: en la computadora 1 abre una segunda ventana
y ejecuta `.\scripts\frontend-start.ps1 -ApiUrl http://localhost:5100`, luego
<http://localhost:5200>. La arquitectura (frontend → API → Odoo) es la misma; solo cambia que los
procesos comparten equipo.

---

## 5. Checklist del día

- [ ] Ambas computadoras cargadas y en la misma red (idealmente hotspot).
- [ ] `git pull` en ambas con la última versión.
- [ ] Computadora 1: Docker Desktop abierto → `backend-start.ps1` → IP anotada.
- [ ] Computadora 2: `Test-NetConnection IP -Port 5100` → `True`.
- [ ] Computadora 2: `frontend-start.ps1 -ApiUrl http://IP:5100` → "API alcanzable".
- [ ] Login con el cajero → **● Odoo conectado**.
- [ ] Odoo abierto en `localhost:8069` (computadora 1) para mostrar las órdenes.
- [ ] Sesión de caja abierta (si dice *Caja cerrada*: `odoo-setup.ps1`).
- [ ] Plan B probado: todo en la computadora 1.
