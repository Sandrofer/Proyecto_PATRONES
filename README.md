# POS integrado con Odoo 20 Community

Proyecto integrador de **Patrones de Software**. Punto de venta web para una cafetería:

```
Navegador (Blazor WebAssembly) ──HTTP──►  API REST .NET 10  ──JSON-2──►  Odoo 20 Community (Docker)
   computadora FRONTEND                    computadora BACKEND             computadora BACKEND
```

El frontend **nunca** habla con Odoo; la API key de Odoo solo existe en el backend.

## Estructura

| Carpeta | Contenido |
|---|---|
| `backend/src/Pos.Domain` | Venta (agregado), líneas, impuestos, políticas de descuento (Strategy). Sin dependencias. |
| `backend/src/Pos.Application` | Casos de uso (input ports + interactors) y puertos de salida (`IPosGateway`, `IIdempotencyStore`…). |
| `backend/src/Pos.Infrastructure` | Adaptador de Odoo JSON-2, cajeros (usuarios de Odoo), idempotencia, JWT. |
| `backend/src/Pos.Api` | Endpoints REST, ProblemDetails, CORS, composición (DI). |
| `backend/src/Pos.Shared` | Contratos (DTOs) compartidos con el frontend. |
| `frontend/src/Pos.Frontend.Domain` | Carrito y total provisional (C# puro). |
| `frontend/src/Pos.Frontend.Application` | Casos de uso del frontend (login, checkout) y puerto `IPosApi`. |
| `frontend/src/Pos.Frontend.Infrastructure` | `HttpPosApi` (único lugar que conoce HTTP), sesión del navegador. |
| `frontend/src/Pos.Frontend.Web` | Páginas Blazor (presentación). |
| `docker/` | `docker-compose.yml` (Odoo 20 + PostgreSQL) y `setup/setup_pos.py`. |
| `scripts/` | Scripts de preparación y arranque. |
| `docs/decisiones-diseno.md` | Registro de decisiones de diseño (patrones). |
| `docs/diagramas.md` | Diagramas: despliegue, capas, Strategy, secuencia del cobro y estados de la venta. |

## Requisitos previos

- **Backend:** Windows 10/11, [.NET SDK 10](https://dotnet.microsoft.com/download), Docker Desktop.
- **Frontend:** .NET SDK 10 y un navegador moderno.
- Ambas computadoras en la **misma red** (misma Wi-Fi o cable). Copie el repositorio completo en las dos.

## Computadora BACKEND (una sola vez)

```powershell
cd C:\dev\pos-odoo
.\scripts\odoo-setup.ps1        # Odoo + datos + API key (tarda ~3 min la 1.ª vez)
```

**Cajeros = usuarios de Odoo.** Créelos en Odoo (`http://localhost:8069`, usuario `admin`):
**Ajustes → Usuarios → Nuevo**, con nombre, usuario y contraseña, y en *Punto de venta* el
permiso **Usuario**. Para quitar el acceso basta con archivar el usuario o quitarle ese permiso.
La API valida la contraseña con Odoo, comprueba el grupo `point_of_sale.group_pos_user`
(sin él responde 403) y registra cada venta a nombre del cajero (`user_id` del pedido).

`odoo-setup.ps1` crea (sin duplicar si se repite): impuesto **IVA 15 %**, 4 productos vendibles
(+1 retirado para la prueba P04) con stock, método de pago **Efectivo**, el punto de venta
**Cafetería - Caja 1**, una **sesión abierta**, el usuario de integración `pos_integration`
y su **API key** (scope `rpc`), que guarda en `dotnet user-secrets` (fuera del repositorio).

Firewall (una vez, PowerShell **como administrador**):

```powershell
New-NetFirewallRule -DisplayName "POS API 5100" -Direction Inbound -Protocol TCP -LocalPort 5100 -Action Allow -Profile Private,Domain
```

## Arranque para la presentación

**Backend:**
```powershell
.\scripts\backend-start.ps1
```
Muestra la IP que debe usar el frontend, por ejemplo `http://192.168.1.20:5100`.

**Frontend** (la otra computadora):
```powershell
.\scripts\frontend-start.ps1 -ApiUrl http://192.168.1.20:5100
```
Abra **http://localhost:5200** e inicie sesión con un usuario de Odoo que tenga permiso de Punto de venta.

Odoo (administración): `http://IP-BACKEND:8069`. Si la sesión POS se cerró en Odoo, vuelva a
ejecutar `.\scripts\odoo-setup.ps1` (abre una sesión nueva).

## Variables de configuración (backend)

| Clave | Dónde | Descripción |
|---|---|---|
| `Odoo:BaseUrl` | appsettings.json | `http://localhost:8069` |
| `Odoo:Database` | appsettings.json | `pos` |
| `Odoo:ApiKey` | **user-secrets** | API key del usuario de integración |
| `Odoo:PosConfigId` | user-secrets | id del punto de venta |
| `Jwt:SigningKey` | **user-secrets** | clave de firma de los tokens (≥ 32 caracteres) |
| `Cors:AllowedOrigins` | appsettings.json | `["*"]` o la URL del frontend |
| `Discounts:*` | appsettings.json | máximo manual, % estudiante, volumen |

Cualquier clave puede darse también como variable de entorno (`Odoo__ApiKey`, etc.).
`docker/.env` (contraseña de PostgreSQL) se genera solo y está en `.gitignore`.

## API REST

| Método | Ruta | Propósito |
|---|---|---|
| POST | `/api/auth/login` | Login del cajero → JWT |
| GET | `/api/products?search=` | Catálogo (nombre, código o código de barras) |
| GET | `/api/pos/sessions/active` | Sesión POS abierta y métodos de pago |
| GET | `/api/discount-policies` | Políticas de descuento habilitadas |
| POST | `/api/sales/quote` | Total verificado con precios/impuestos de Odoo (no registra) |
| POST | `/api/sales` | Registra venta + pago en efectivo. Cabecera **`Idempotency-Key`** obligatoria |
| GET | `/api/sales` | Historial |
| GET | `/api/sales/{id}` | Detalle |
| GET | `/api/customers/{cedula}` | Busca un cliente en Odoo por cédula (400 si la cédula no es válida, 404 si no existe) |

Respuestas de `POST /api/sales`: **201** registrada · **200** ya estaba registrada (misma clave) ·
**202** resultado incierto (Odoo no respondió: reintentar con la misma clave) · **400** datos
inválidos · **409** la misma operación está en curso · **502/503** Odoo rechazó / no disponible.
Errores en formato ProblemDetails, en español.

## Pruebas

```powershell
cd backend;  dotnet test tests\Pos.UnitTests                 # 113 pruebas: dominio, casos de uso, adaptador Odoo, login, stock, cédula, arquitectura
cd frontend; dotnet test tests\Pos.Frontend.UnitTests        # 44 pruebas: carrito (P07), errores de la API (P08), checkout, stock, cliente

# P09 con Odoo REAL (crea 1 venta en Odoo):
cd backend; $env:POS_RUN_INTEGRATION="1"; dotnet test tests\Pos.IntegrationTests
```

| ID | Prueba | Dónde |
|---|---|---|
| P01 | Venta sin productos | `SaleTests`, `RegisterSaleInteractorTests` |
| P02 | Cantidad cero o negativa | `SaleTests`, `RegisterSaleInteractorTests` |
| P03 | Sesión POS cerrada / inexistente | `RegisterSaleInteractorTests` |
| P04 | Producto no habilitado; no se envía a Odoo | `SaleTests`, `RegisterSaleInteractorTests` |
| P05 | Error / timeout de Odoo sin declarar éxito | `RegisterSaleInteractorTests` |
| P06 | Repetición y concurrencia sin segunda venta | `RegisterSaleInteractorTests` |
| P07 | Carrito, cantidades, total provisional | `frontend/.../CartTests` |
| P08 | Error comprensible si falla la API | `frontend/.../ApiErrorAndCheckoutTests` |
| P09 | Venta real de 2 productos en efectivo | `Pos.IntegrationTests` |
| P10 | Nueva política de descuento sin tocar otros componentes | `DiscountExtensibilityTests` |
| Extra | No vender más que el stock de Odoo (RN-09) | `StockTests`, `frontend/.../CartStockTests` |
| Extra | Cliente con cédula ecuatoriana válida (RN-10) | `CustomerTests`, `frontend/.../CheckoutCustomerTests` |
| Extra | Condiciones de cada descuento y límites de la venta | `DiscountRulesTests`, `frontend/.../DiscountValidationTests` |
| Extra | Efectivo recibido y vuelto | `CashTenderTests` (backend y frontend) |
| Extra | Caracteres especiales, usuario y decimales del descuento | `InputRulesTests`, `frontend/.../LoginInputTests` |
| Extra | Cantidades enteras (RN-11) | `WholeQuantityTests`, `frontend/.../CartWholeQuantityTests` |
| Extra | Login con usuarios de Odoo y permiso de POS | `LoginTests`, `OdooOperatorDirectoryTests` |

### Reglas adicionales

- **RN-09 Stock.** Un producto almacenable no se vende por encima del stock que reporta Odoo
  (`Sale.AddProduct`). El navegador marca los productos agotados y no deja pasar del disponible,
  y la API lo vuelve a comprobar con el stock actual antes de registrar.
- **RN-10 Cliente.** La venta es a *consumidor final* o a un cliente con **cédula ecuatoriana
  válida** (10 dígitos, provincia 01–24 o 30, tercer dígito < 6 y dígito verificador módulo 10) y
  nombre de solo letras (3–80). El cliente se busca en Odoo por cédula (`res.partner.vat`) o se crea
  ahí, y la venta queda con su `partner_id`. Para crear clientes, el usuario de integración tiene el
  permiso *Creación de contactos* (lo asigna `odoo-setup.ps1`).
- **Validación de entradas (RNF-09).** Reglas compartidas por API y navegador (`Pos.Shared/InputRules.cs`):
  la búsqueda de productos y el filtro del historial solo admiten letras (con tildes y ñ), números,
  espacios y `- . _ /` (máx. 50); el usuario del login solo letras, números y `. _ - @`. Con otros
  caracteres (`!"#*<>%'`...) la API responde 400 sin consultar Odoo.
- **Descuentos (RF-06/07/08, RN-04).** Solo políticas habilitadas, cada una con sus condiciones:
  - *Manual*: número mayor que 0, hasta el máximo autorizado (10 %), con 2 decimales como máximo.
  - *Volumen*: solo si la compra (antes del descuento) llega a $ 100.
  - *Estudiante*: solo con cliente identificado con cédula (nunca a consumidor final).
  Cada política publica sus condiciones (`minimumAmount`, `requiresIdentifiedCustomer`) en
  `GET /api/discount-policies`; el navegador avisa al instante, no aplica el descuento al total
  provisional y deshabilita *Verificar* y *Cobrar*. La API vuelve a comprobarlo (400).
- **Límites de la venta.** Máximo 999 unidades por producto y 50 productos distintos.
- **Cliente.** Si se elige *Con cédula*, la cédula y el nombre son obligatorios: la venta no se
  envía en silencio como consumidor final.
- **Método de pago (RF-11).** Debe estar configurado en la sesión POS abierta de Odoo y ser de
  efectivo; cualquier otro id se rechaza (400).
- **Efectivo recibido y vuelto (RF-11).** Campo opcional *Efectivo recibido* (vacío = monto exacto)
  con botones de billetes sugeridos. Debe ser mayor que 0, con centavos como máximo, cubrir el
  total y dejar un vuelto de hasta $ 100 (evita errores como 500 en vez de 5,00). La pantalla
  muestra el vuelto al instante; Odoo guarda el pago recibido y la línea de cambio, como su propio
  POS (pago $ 5,00, vuelto −$ 2,93).
- **RN-11 Cantidades enteras.** Los productos se venden por unidad: no se acepta 1,5 cafés.
  El carrito lo impide y la API responde 400 indicando la línea (`lines[i].quantity`).

Verificación manual realizada contra Odoo 20 (`20.0-20261112`): venta registrada como `paid`,
pago en efectivo, stock descontado, reenvío con la misma clave (también tras reiniciar la API)
sin duplicar, dos peticiones simultáneas (una recibe 409) y Odoo apagado (503, sin éxito falso).
