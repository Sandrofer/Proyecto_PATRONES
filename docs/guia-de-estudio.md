# Guía de estudio del proyecto

Todo lo que cada integrante debe entender para la **defensa técnica individual**. El enunciado
dice: *"Cada estudiante debe poder explicar y modificar el código que presenta"* y *"La defensa
puede incluir un cambio pequeño de requisito"* (CA-08). Esta guía está ordenada para estudiar de
lo general a lo particular.

**Índice**
1. [El proyecto en un minuto](#1-el-proyecto-en-un-minuto)
2. [Qué pide el enunciado y dónde se cumple](#2-qué-pide-el-enunciado-y-dónde-se-cumple)
3. [Arquitectura (Clean Architecture)](#3-arquitectura-clean-architecture)
4. [Recorrido de una venta, archivo por archivo](#4-recorrido-de-una-venta-archivo-por-archivo)
5. [Odoo: qué es y cómo nos integramos](#5-odoo-qué-es-y-cómo-nos-integramos)
6. [Decisiones de diseño y patrones (D1–D8)](#6-decisiones-de-diseño-y-patrones-d1d8)
7. [SOLID y Clean Code](#7-solid-y-clean-code)
8. [Reglas de negocio y validaciones](#8-reglas-de-negocio-y-validaciones)
9. [Seguridad](#9-seguridad)
10. [Pruebas](#10-pruebas)
11. [Preguntas probables de la defensa](#11-preguntas-probables-de-la-defensa)
12. [Cambios de requisito para practicar](#12-cambios-de-requisito-para-practicar)
13. [Glosario](#13-glosario)
14. [Plan de estudio sugerido](#14-plan-de-estudio-sugerido)

Documentos complementarios: [decisiones-diseno.md](decisiones-diseno.md) (detalle de cada
decisión), [diagramas.md](diagramas.md), [conexion-dos-computadoras.md](conexion-dos-computadoras.md),
[README](../README.md) (API, pruebas, reglas).

---

## 1. El proyecto en un minuto

Un **punto de venta web** para una cafetería. El cajero busca productos, arma el carrito, aplica
un descuento autorizado, identifica al cliente (opcional), cobra en efectivo y obtiene el
comprobante. **Odoo 20** es el sistema central de la empresa: tiene los productos, precios,
impuestos, stock, usuarios, clientes y guarda las ventas. Nosotros **no duplicamos** eso: lo
consultamos y le registramos las ventas.

```
Navegador (Blazor WebAssembly, C#)  ──HTTP/JSON──►  API REST ASP.NET Core (.NET 10)  ──JSON-2──►  Odoo 20 (Docker)
```

Tres frases para empezar cualquier explicación:
1. *"El frontend nunca habla con Odoo; solo con nuestra API, que es la única que tiene la API key."*
2. *"Las reglas de negocio están en el dominio (`Sale`, políticas de descuento, `CashTender`),
   no en los endpoints ni en la pantalla."*
3. *"Una venta nunca se duplica ni se informa como exitosa si Odoo no lo confirmó (clave de
   idempotencia + verificación en Odoo)."*

---

## 2. Qué pide el enunciado y dónde se cumple

### Requisitos funcionales

| Código | Requisito | Cómo se cumple | Dónde mirar |
|---|---|---|---|
| RF-01 | Consultar productos desde Odoo | `GET /api/products` lee `product.product` disponibles en POS | `SearchProductsInteractor`, `OdooPosGateway.SearchProductsAsync` |
| RF-02 | Buscar por nombre o código; precio y disponibilidad | Búsqueda por nombre, código interno o de barras; muestra precio, IVA y stock | `PosPage.razor`, `InputRules.ValidateSearch` |
| RF-03/04 | Agregar, cambiar cantidades, eliminar | Carrito en el dominio del frontend | `Pos.Frontend.Domain/Cart.cs` |
| RF-05 | Subtotales y total coherentes | Total provisional en el navegador + **total verificado** por la API con precios e impuestos de Odoo y su modo de redondeo | `Sale.CalculateTotals`, `POST /api/sales/quote` |
| RF-06 | Aplicar políticas de descuento habilitadas | Patrón **Strategy** | `DiscountPolicies.cs` |
| RF-07 | Agregar/cambiar políticas sin impacto | Una clase nueva + una línea de registro; prueba P10 | `DiscountExtensibilityTests` |
| RF-08 | Rechazar descuentos no autorizados con mensajes claros | Cada política devuelve el motivo; el navegador lo muestra al instante | `DiscountDecision.Reject`, `CheckoutUseCase.ValidateDiscount` |
| RF-09 | Confirmar venta con líneas, precios, descuentos | `Sale.Confirm()` | `Sale.cs`, `RegisterSaleInteractor` |
| RF-10 | Método de pago configurado | Se valida contra los métodos de la sesión de Odoo; solo efectivo | `RegisterSaleInteractor` |
| RF-11 | Registrar venta y pago en Odoo | `pos.order.sync_from_ui` con pago y vuelto | `OdooPosGateway.SubmitPaidOrderAsync` |
| RF-12 | Mostrar identificador, estado y resultado real | Se **relee** la orden en Odoo y se muestra su número y estado reales | `FindAnyOrderAsync` |
| RF-13/14 | Historial y detalle | `GET /api/sales`, `GET /api/sales/{id}` | `SalesPage.razor`, `SaleDetailPage.razor` |
| RF-15 | Impedir datos inválidos | Validación en navegador **y** API (cantidades, stock, cédula, descuentos, efectivo, caracteres) | `InputRules`, `SaleBuilder`, `Sale` |
| RF-16 | Evitar duplicados | **Idempotency-Key** = `uuid` de la orden en Odoo | `RegisterSaleInteractor`, `InMemoryIdempotencyStore` |
| RF-17 | Errores de comunicación sin falso éxito | 202 "incierto" + reintento seguro; aviso global de Odoo caído | D3 y D8 |

> Nota: la numeración del PDF está desalineada en la tabla; lo importante es el contenido de cada requisito.

### Reglas de negocio (RN-01 a RN-08) y las que agregamos

| Regla | Dónde se garantiza |
|---|---|
| RN-01 Al menos un producto | `Sale.Confirm()`; botones deshabilitados con carrito vacío |
| RN-02 Cantidades > 0 | `Sale.AddProduct`, `SaleBuilder.ValidateShape`, `Cart` |
| RN-03 Precios vienen de Odoo | La API **ignora** precios del navegador: solo recibe `productId` y cantidad y vuelve a leer el precio en Odoo |
| RN-04 Descuentos autorizados | Strategy + `Sale.ApplyDiscount` solo acepta decisiones aprobadas |
| RN-05 No confirmar venta inconsistente | Validaciones del agregado antes de `Confirm()` |
| RN-06 Venta confirmada no se edita | `EnsureInPreparation()` (D4) |
| RN-07 Sin pago exitoso sin confirmación | Se relee la orden; si Odoo no responde → "incierto" |
| RN-08 Petición repetida no duplica | Idempotency-Key (D3) |
| RN-09 *(nuestra)* Stock | No vender más que el stock de Odoo |
| RN-10 *(nuestra)* Cliente con cédula ecuatoriana válida | `Cedula` (algoritmo módulo 10) |
| RN-11 *(nuestra)* Cantidades enteras | Productos por unidad |

### No funcionales y criterios de aceptación

| RNF / CA | Evidencia en el proyecto |
|---|---|
| RNF-01/02, CA-04 Clean Architecture | Proyectos separados por capa; `ArchitectureTests`; casos de uso del frontend sin Blazor ni HttpClient |
| RNF-03, CA-05 Patrones justificados | `decisiones-diseno.md` (D1–D8) con problema, alternativas, elección, consecuencias, código y prueba |
| RNF-04/05 SOLID, Clean Code | Sección 7 de esta guía |
| RNF-06, CA-07 Pruebas | 116 + 50 pruebas unitarias + 1 de integración real |
| RNF-07 Extensibilidad | P10 (nueva política sin tocar nada) |
| RNF-08 Errores consistentes | ProblemDetails en español (`ErrorHandling.cs`) |
| RNF-09 Seguridad | user-secrets, JWT, validación de entradas, límite de intentos de login |
| RNF-11 Interoperabilidad | Flujo navegador → .NET → Odoo demostrado con dos computadoras |
| RNF-12 Reproducibilidad | `INSTALACION.md`, scripts `odoo-setup` / `backend-start` / `frontend-start` |
| Sección 12 (exclusiones) | No hacemos facturación electrónica, pasarela bancaria, contabilidad ni crédito: Odoo lo maneja |

---

## 3. Arquitectura (Clean Architecture)

**Regla de dependencia:** el código solo depende **hacia adentro** (hacia el dominio). El dominio
no conoce a nadie; la infraestructura (Odoo, HTTP) depende de la aplicación, nunca al revés.

### Backend (`backend/src`)

| Proyecto | Responsabilidad | Ejemplos | Depende de |
|---|---|---|---|
| `Pos.Domain` | Reglas de negocio puras | `Sale`, `SaleLine`, `IDiscountPolicy` y sus 4 políticas, `Customer`, `Cedula`, `CashTender` | Nadie |
| `Pos.Application` | Casos de uso y **puertos** (interfaces) | `RegisterSaleInteractor`, `SaleBuilder`, `LoginInteractor`, `IPosGateway`, `IOperatorDirectory`, `IIdempotencyStore` | Domain, Shared |
| `Pos.Infrastructure` | **Adaptadores** a lo externo | `OdooPosGateway`, `OdooJson2Client`, `OdooOperatorDirectory`, `InMemoryIdempotencyStore`, JWT | Application |
| `Pos.Api` | Presentación HTTP | `Endpoints.cs` (sin lógica), `ErrorHandling.cs`, `Program.cs` (composición) | Application, Infrastructure |
| `Pos.Shared` | Contratos compartidos | DTOs (`SaleRequest`, `SaleDetailDto`…), `ApiRoutes`, `InputRules` | Nadie |

Casos de uso (input ports): `ILoginInputPort`, `ISearchProductsInputPort`,
`IGetActiveSessionInputPort`, `IGetDiscountPoliciesInputPort`, `IQuoteSaleInputPort`,
`IRegisterSaleInputPort`, `IGetSalesHistoryInputPort`, `IGetSaleDetailInputPort`,
`IFindCustomerInputPort`, `IGetSystemStatusInputPort`.

### Frontend (`frontend/src`)

| Proyecto | Responsabilidad | Ejemplos |
|---|---|---|
| `Pos.Frontend.Domain` | Reglas del lado del cliente | `Cart`, `Cedula`, `CashTender` |
| `Pos.Frontend.Application` | Casos de uso y puerto `IPosApi` | `LoginUseCase`, `CheckoutUseCase`, `ConnectivityState`, `SystemStatusUseCase` |
| `Pos.Frontend.Infrastructure` | Único que conoce HTTP | `HttpPosApi`, `BrowserAuthSession` |
| `Pos.Frontend.Web` | Pantallas Blazor | `PosPage`, `SalesPage`, `SaleDetailPage`, `LoginPage`, `MainLayout`, `PrintableReceipt` |

**¿Por qué Blazor?** El enunciado acepta "otra tecnología justificada". Con Blazor WebAssembly
el frontend también es C#: compartimos los contratos (`Pos.Shared`) y las reglas de formato
(`InputRules`) con la API sin escribirlas dos veces, y podemos probar los casos de uso del
frontend con xUnit igual que el backend.

**¿Cómo se comprueba?** `ArchitectureTests` falla si `Pos.Domain` referencia otro proyecto o
si `Pos.Application` referencia Infrastructure, la API o ASP.NET.

**Decisión consciente:** no usamos *Presenter* como en el ejemplo NorthWind: los interactors
devuelven DTOs directamente porque solo hay una salida (JSON) y el presenter solo copiaba datos.

---

## 4. Recorrido de una venta, archivo por archivo

Ensayen contar esto en voz alta; es la pregunta más probable.

1. **Login** — `LoginPage` → `LoginUseCase` → `POST /api/auth/login` → `LoginInteractor` →
   `OdooOperatorDirectory` valida usuario y contraseña **contra Odoo**
   (`/web/session/authenticate`) y verifica el grupo *Punto de venta / Usuario* (`has_group`).
   La API emite un **JWT** con el nombre y el id de Odoo del cajero.
2. **Catálogo** — `PosPage` → `GET /api/products?search=` → `SearchProductsInteractor` →
   `OdooPosGateway` lee `product.product`, impuestos (`account.tax`) y stock.
3. **Carrito** — `Cart` (frontend) controla cantidades enteras, stock, máx. 999 por línea y 50
   líneas; calcula un **total provisional**.
4. **Descuento, cliente, efectivo** — `CheckoutUseCase` valida en vivo con las condiciones que
   publica cada política (`GET /api/discount-policies`), la cédula y el efectivo recibido.
   Si algo está mal: aviso inmediato y botones deshabilitados.
5. **Verificar total** (opcional) — `POST /api/sales/quote` → `QuoteSaleInteractor` construye la
   venta con precios **reales de Odoo** sin registrarla.
6. **Cobrar** — `CheckoutUseCase.ConfirmAsync` genera un **GUID** (Idempotency-Key) y llama a
   `POST /api/sales` con la cabecera `Idempotency-Key`.
7. **`RegisterSaleInteractor`**:
   1. Reserva la clave en `InMemoryIdempotencyStore` (si ya está en curso → **409**).
   2. Busca en Odoo una orden con ese `uuid` (si existe → **200** "ya registrada", no duplica).
   3. Comprueba sesión de caja abierta y método de pago en efectivo.
   4. `SaleBuilder` arma el agregado `Sale`: productos y precios desde Odoo, stock, cliente,
      descuento (Strategy). Cualquier regla rota → **400** con mensaje claro.
   5. `Sale.Confirm()` y `CashTender.Create()` (vuelto).
   6. `OdooPosGateway.SubmitPaidOrderAsync` → `pos.order.sync_from_ui` con líneas, cliente,
      cajero (`user_id`), pago recibido y `amount_return` (vuelto).
   7. **Relee** la orden en Odoo y devuelve su número y estado reales → **201**.
   8. Si Odoo no respondió: vuelve a buscar la orden; si sigue sin saber → **202 incierto**.
8. **Pantalla** — Recibo con número, estado y vuelto; botones *Ver detalle* e *Imprimir*.
   Si fue incierto: *Reintentar* (misma clave, no duplica) o *Descartar*.

Diagrama de secuencia: [diagramas.md §4](diagramas.md#4-secuencia-del-cobro-rf-11-rf-1617-rn-07).

---

## 5. Odoo: qué es y cómo nos integramos

**Odoo** es un ERP (sistema de gestión empresarial) de código abierto. La edición *Community* es
gratuita. Tiene módulos: Ventas, Inventario, Contabilidad, Punto de venta… Lo ejecutamos en
**Docker** (contenedores `pos-odoo-web` y `pos-odoo-db` con PostgreSQL).

**Qué hace Odoo en este proyecto:** es la **fuente de verdad**. Guarda productos, precios,
impuestos, stock, usuarios (cajeros), clientes, sesiones de caja y las órdenes pagadas. Nuestro
POS es una "caja registradora" que se conecta a él.

**Cómo nos conectamos:**
- **API JSON-2** de Odoo 20: `POST /json/2/{modelo}/{método}` con `Authorization: bearer <API key>`.
- La API key pertenece al usuario técnico `pos_integration` y vive en **user-secrets** del backend.
- **Nunca** accedemos a la base PostgreSQL directamente (lo prohíbe el enunciado y saltaría las reglas de Odoo).

**Modelos de Odoo que usamos:**

| Modelo | Para qué |
|---|---|
| `product.product` | Catálogo, precio, stock, si está disponible en POS |
| `account.tax` | IVA 15 % y si el precio lo incluye |
| `pos.config` / `pos.session` | El punto de venta "Cafetería - Caja 1" y su sesión abierta |
| `pos.payment.method` | Métodos de pago de la sesión (efectivo) |
| `pos.order` / `pos.order.line` | La venta y sus líneas (`sync_from_ui` crea la orden pagada) |
| `pos.payment` | El pago y la línea de vuelto (`is_change`) |
| `res.partner` | Clientes (la cédula va en el campo `vat`) |
| `res.users` | Cajeros y su permiso de Punto de venta |
| `res.company` | Datos de la empresa para el comprobante |

**Hallazgos reales al integrar** (buenos para la defensa; demuestran que se probó de verdad):
- JSON-2 ignoraba cuerpos *chunked*: hay que enviar `Content-Length`.
- La API key necesita *scope* `rpc` en Odoo 20.
- Odoo recalcula el total: usamos su mismo modo de redondeo (`round_globally`).
- El vuelto se envía como hace el propio POS de Odoo: pago recibido + `amount_return` negativo.

**Recorrido rápido por Odoo (en español):** *Punto de venta → Órdenes → Órdenes / Sesiones /
Clientes*; *Inventario → Reportes → Existencias*; *Ajustes → Usuarios y empresas → Usuarios*.

---

## 6. Decisiones de diseño y patrones (D1–D8)

El enunciado pide **al menos tres** decisiones con: problema, alternativas, elección y categoría,
consecuencias, código y prueba. Tenemos ocho. Detalle completo en
[decisiones-diseno.md](decisiones-diseno.md). Distingan siempre la **categoría**:
patrón GoF, patrón empresarial/de integración, enfoque arquitectónico, principio o práctica.

| # | Decisión | Categoría | Problema → solución en una línea |
|---|---|---|---|
| D1 | **Strategy** para descuentos | Patrón GoF | Varios descuentos que cambian → una clase por regla (`IDiscountPolicy`); agregar uno no toca `Sale` |
| D2 | **Adapter / Gateway** + Ports & Adapters para Odoo | Patrón empresarial | Odoo tiene formato propio → `IPosGateway` en Application, `OdooPosGateway` en Infrastructure |
| D3 | **Idempotency Key** | Patrón de integración | Doble clic o timeout podían duplicar → GUID por venta usado como `uuid` en Odoo |
| D4 | **Aggregate** `Sale` con estados (en vez de *State*) | DDD | 2 estados y 1 transición → un enum y una guarda; *State* sería sobrediseño |
| D5 | **Clean Architecture** en ambas apps | Enfoque arquitectónico | Separar reglas de frameworks y de Odoo |
| D6 | **Value Object** `CashTender` | DDD | Vuelto inválido o errores de tipeo → solo se crea con montos válidos |
| D7 | Validar en navegador **y** API | Práctica (RNF-09) | Respuesta inmediata sin confiar en el navegador; reglas de formato compartidas en `Pos.Shared` |
| D8 | **Health Check + Observer** para Odoo caído | Patrón de integración + GoF | Un estado único (`ConnectivityState`) alimentado por cada respuesta y por `/api/status`; la UI se suscribe |

### Cómo explicar Strategy (D1) en 30 segundos
> "Teníamos descuento manual con tope, de estudiante y por volumen, y el negocio pedirá más. Con
> un `switch` cada descuento nuevo obligaría a modificar `Sale`. Usamos Strategy: la interfaz
> `IDiscountPolicy` con `Evaluate(contexto)` y una clase por regla. `Sale.ApplyDiscount` solo
> acepta decisiones aprobadas. Agregar un descuento es una clase y una línea en
> `DependencyContainer`; la prueba P10 agrega `EmployeeDiscountPolicy` sin tocar nada más. Además,
> cada política publica sus condiciones (`MinimumAmount`, `RequiresIdentifiedCustomer`) y la
> pantalla las valida sin conocer cada regla."

### Cómo explicar Idempotency Key (D3) en 30 segundos
> "Si el cajero hace doble clic o la red se corta después de que Odoo guardó la venta, un
> reintento podría crear dos ventas. El navegador genera un GUID por venta y lo manda en la
> cabecera `Idempotency-Key`. La API lo usa como `uuid` de la orden en Odoo y antes de crear
> pregunta si ya existe. Si Odoo no responde, no decimos 'éxito': respondemos 'incierto' y el
> reintento con la misma clave es seguro. Odoo además tiene `unique(uuid)`."

### Cuándo **no** usar un patrón (también se evalúa)
- D4: *State* no aporta con dos estados.
- Sin *Presenter*: con una sola salida JSON solo copiaba datos.
- Idempotencia en memoria y no en Redis: suficiente para una caja; lo decimos como compromiso.

---

## 7. SOLID y Clean Code

| Principio | Dónde lo aplicamos de verdad |
|---|---|
| **S** – Responsabilidad única | `SaleBuilder` construye, `RegisterSaleInteractor` coordina, `OdooPosGateway` traduce, `OdooJson2Client` transporta |
| **O** – Abierto/cerrado | Nuevas políticas de descuento sin modificar `Sale` ni los casos de uso; miembros por defecto en la interfaz |
| **L** – Sustitución de Liskov | Cualquier `IDiscountPolicy` o `IPosGateway` (real o doble de prueba) es intercambiable |
| **I** – Segregación de interfaces | Un input port por caso de uso; puertos pequeños (`IIdempotencyStore`, `ITokenIssuer`, `IOperatorDirectory`) |
| **D** – Inversión de dependencias | Application define `IPosGateway`; Infrastructure lo implementa; se conectan en `DependencyContainer` |

**Clean Code:** nombres del dominio en inglés y mensajes al usuario en español; métodos cortos;
objetos de valor (`Cedula`, `CashTender`) en lugar de `string`/`decimal` sueltos; errores
explícitos (`ValidationException`, `ExternalServiceUnavailableException`) traducidos a
ProblemDetails en un solo lugar; endpoints sin lógica; constantes con nombre
(`MaxQuantityPerLine`, `MaxChange`).

---

## 8. Reglas de negocio y validaciones

Principio: **el navegador avisa al instante, la API decide** (D7).

| Validación | Regla | Mensaje (ejemplo) |
|---|---|---|
| Cantidad | Entera, > 0, ≤ 999 por línea, ≤ 50 productos distintos | "La cantidad debe ser un número entero." |
| Stock | No más que lo disponible en Odoo | "Stock insuficiente de 'Croissant': se piden 10 y hay 6." |
| Descuento manual | > 0, ≤ 10 %, máx. 2 decimales | "El descuento máximo autorizado es 10 %." |
| Descuento por volumen | Compra ≥ $100 antes del descuento | "requiere una compra mínima de $ 100,00 (total actual $ 2,07)" |
| Descuento estudiante | Cliente con cédula | "requiere identificar al cliente con su cédula" |
| Cédula | 10 dígitos, provincia 01–24 o 30, tercer dígito < 6, dígito verificador módulo 10 | "La cédula no es válida…" |
| Nombre del cliente | 3–80 letras (con tildes) y `. ' -` | "solo puede contener letras" |
| Efectivo recibido | > 0, centavos, cubre el total, vuelto ≤ $100 | "no cubre el total" |
| Método de pago | Configurado en la sesión y de efectivo | "Solo se admite el pago en efectivo…" |
| Búsqueda / usuario | Solo letras, números y símbolos permitidos | "La búsqueda solo admite…" |
| Sesión de caja | Debe estar abierta en Odoo | "No hay una sesión POS abierta…" |

**Algoritmo de la cédula (módulo 10)** — por si lo preguntan: se multiplican los 9 primeros
dígitos por 2,1,2,1,…; si un producto pasa de 9 se le resta 9; se suman; el verificador es
`(10 − suma mod 10) mod 10` y debe ser igual al décimo dígito.

---

## 9. Seguridad

- **Secretos fuera del repositorio:** API key de Odoo y clave JWT en `dotnet user-secrets`;
  contraseña de PostgreSQL en `docker/.env` (en `.gitignore`).
- **El navegador no tiene la API key**: solo un JWT del cajero (expira en 8 h).
- **Cajeros = usuarios de Odoo**: no guardamos contraseñas; Odoo las valida. Sin el permiso de
  Punto de venta → 403.
- **Límite de intentos de login:** 10 por minuto (`RateLimiter`), luego "Demasiados intentos".
- **Validación de entradas** en ambos lados (RNF-09) y la API **nunca** confía en precios ni
  totales que mande el navegador.
- **Errores** sin detalles internos (ProblemDetails con mensajes para el usuario).

---

## 10. Pruebas

```powershell
cd backend;  dotnet test tests\Pos.UnitTests             # 116 pruebas
cd frontend; dotnet test tests\Pos.Frontend.UnitTests    # 50 pruebas
cd backend; $env:POS_RUN_INTEGRATION="1"; dotnet test tests\Pos.IntegrationTests   # venta REAL en Odoo
```

| ID | Qué prueba | Archivo |
|---|---|---|
| P01 | Venta sin productos | `SaleTests`, `RegisterSaleInteractorTests` |
| P02 | Cantidad cero o negativa | `SaleTests` |
| P03 | Sesión cerrada | `RegisterSaleInteractorTests` |
| P04 | Producto no habilitado no se envía a Odoo | `SaleTests` |
| P05 | Timeout/error de Odoo sin falso éxito | `RegisterSaleInteractorTests` |
| P06 | Repetición y concurrencia sin duplicar | `RegisterSaleInteractorTests` |
| P07 | Carrito y total provisional | `CartTests` (frontend) |
| P08 | Error comprensible si falla la API | `ApiErrorAndCheckoutTests` (frontend) |
| P09 | Venta real de 2 productos en efectivo | `Pos.IntegrationTests` |
| P10 | Nueva política sin tocar nada | `DiscountExtensibilityTests` |

**Técnicas usadas:** dobles de prueba con **NSubstitute** (simulan `IPosGateway` sin Odoo —
posible gracias a D2), handler HTTP falso para probar el adaptador de Odoo
(`OdooPosGatewayTests`), y `ArchitectureTests` para las dependencias entre capas.

---

## 11. Preguntas probables de la defensa

<details><summary><b>¿Por qué el frontend no se conecta directo a Odoo?</b></summary>

Lo exige el enunciado y por seguridad: la API key daría acceso total a Odoo y estaría expuesta en
el navegador. Además, las reglas (stock, descuentos, idempotencia) deben estar en un solo lugar
confiable.
</details>

<details><summary><b>¿Qué pasa si el cajero hace doble clic en Cobrar?</b></summary>

El botón se deshabilita mientras procesa, y aunque llegaran dos peticiones tienen la misma
Idempotency-Key: la segunda recibe 409 (en curso) o 200 (ya registrada). Nunca se crea otra orden
(P06).
</details>

<details><summary><b>¿Qué pasa si Odoo se cae a mitad del cobro?</b></summary>

La API vuelve a preguntar a Odoo por el `uuid`. Si la encuentra → éxito real. Si no puede saberlo
→ 202 "incierto"; la pantalla muestra *Reintentar* (misma clave, sin duplicar). Además el
indicador se pone rojo y bloquea nuevos cobros hasta que Odoo vuelve (D8).
</details>

<details><summary><b>¿Dónde se calcula el total? ¿Puede el navegador poner otro precio?</b></summary>

El navegador calcula un total **provisional**. La API solo recibe ids y cantidades, lee los
precios e impuestos de Odoo y calcula el total con el mismo redondeo que Odoo. Un precio enviado
por el navegador simplemente no existe en el contrato (RN-03).
</details>

<details><summary><b>¿Por qué Strategy y no un switch?</b></summary>

Con switch, cada descuento nuevo modifica `Sale` y sus pruebas (viola Open/Closed). Con Strategy
es una clase nueva. Desventaja: más clases; por eso no lo usamos donde no hay variación (D4).
</details>

<details><summary><b>¿Por qué no usaron el patrón State para la venta?</b></summary>

Solo hay dos estados (en preparación, confirmada) y una transición. State agregaría clases sin
beneficio. Lo usaríamos si aparecieran "anulada", "reembolsada", "en espera" con comportamiento
distinto.
</details>

<details><summary><b>¿Cómo sabe la API quién es el cajero?</b></summary>

Por el JWT que emitió al iniciar sesión (contiene el `odoo_uid`). Ese id se envía como `user_id`
de la orden, por eso en Odoo cada venta aparece con su cajero.
</details>

<details><summary><b>¿Dónde están las credenciales? ¿Están en GitHub?</b></summary>

No. API key y clave JWT en `dotnet user-secrets` de cada computadora; la contraseña de PostgreSQL
en `docker/.env`, ignorado por Git. `odoo-setup.ps1` las genera en cada instalación.
</details>

<details><summary><b>¿Por qué no hay facturación electrónica ni ventas a crédito?</b></summary>

La sección 12 del enunciado excluye facturación electrónica, pasarelas bancarias y contabilidad, y
la sección 2 pide no duplicar lo que hace Odoo. El comprobante impreso dice "sin validez
tributaria". El crédito sería una cuenta por cobrar (contabilidad): se haría con el método
"Cuenta de cliente" de Odoo y una nueva política de pago, sin cambiar el dominio de la venta.
</details>

<details><summary><b>¿Cómo prueban sin tener Odoo encendido?</b></summary>

Gracias al puerto `IPosGateway`: en las pruebas se reemplaza por un doble (NSubstitute). Solo la
prueba de integración P09 usa Odoo real.
</details>

<details><summary><b>¿Qué es Clean Architecture en una frase?</b></summary>

Organizar el código en capas donde las dependencias apuntan hacia las reglas de negocio, para que
cambiar el framework, la base de datos o el sistema externo no obligue a cambiar las reglas.
</details>

<details><summary><b>¿Por qué validar en el navegador si la API también valida?</b></summary>

Por experiencia de usuario (aviso inmediato, botones bloqueados). Pero el navegador no es
confiable: cualquiera puede llamar a la API directamente, así que la API vuelve a validar todo.
</details>

---

## 12. Cambios de requisito para practicar

La defensa puede pedir un cambio pequeño en vivo. Practiquen estos (cada uno toma minutos):

1. **"El descuento manual máximo ahora es 15 %."**
   Sin tocar código: en `backend/src/Pos.Api/appsettings.json` → `"Discounts": { "ManualMaxPercent": 15 }`.
   Reiniciar la API. La pantalla lo toma sola (lo publica `GET /api/discount-policies`).
2. **"Agregar un descuento de empleado del 20 %."**
   - Crear `EmployeeDiscountPolicy : IDiscountPolicy` en `Pos.Domain/Discounts/DiscountPolicies.cs`
     (copiar el modelo de `StudentDiscountPolicy`; `Code => "EMPLOYEE"`).
   - Registrarlo en `Pos.Infrastructure/DependencyContainer.cs`:
     `services.AddSingleton<IDiscountPolicy>(new EmployeeDiscountPolicy(20m));`
   - Nada más: aparece en la lista del POS. Mostrar la prueba P10 como evidencia.
3. **"El volumen ahora es desde $50 y 8 %."** → `appsettings.json`: `VolumeMinimumAmount: 50`, `VolumePercent: 8`.
4. **"El vuelto máximo es $50."** → `CashTender.MaxChange` en backend
   (`Pos.Domain/Sales/CashTender.cs`) y frontend (`Pos.Frontend.Domain/CashTender.cs`); ajustar la
   prueba `CashTenderTests`.
5. **"Máximo 20 unidades por producto."** → `Sale.MaxQuantityPerLine`, `InputRules.MaxQuantityPerLine`
   y `Cart.MaxQuantityPerLine`.
6. **"El descuento de estudiante también requiere compra mínima de $5."** → en
   `StudentDiscountPolicy` agregar `public decimal? MinimumAmount => 5m;` y la condición en
   `Evaluate`. La pantalla avisa sola por D1.
7. **"Agregar un campo de correo del cliente."** → `SaleCustomerRequest` (Shared), `Customer`
   (Domain, con validación), `FindOrCreatePartnerAsync` (`email` en `res.partner`), formulario en
   `PosPage.razor`. Buen ejercicio para recorrer todas las capas.

Para cada cambio: **modificar → `dotnet test` → probar en pantalla → explicar qué capa tocaron y por qué solo esa.**

---

## 13. Glosario

| Término | Significado |
|---|---|
| **ERP** | Sistema de gestión integral de la empresa (Odoo) |
| **POS** | *Point of Sale*, punto de venta / caja |
| **Sesión de caja** | Turno abierto en Odoo donde se registran las ventas (`pos.session`) |
| **API REST** | Servicio web con recursos y verbos HTTP (GET, POST) |
| **JSON-2** | API nueva de Odoo 19/20: `/json/2/modelo/método` con API key |
| **DTO** | Objeto que solo transporta datos entre capas o por la red |
| **Puerto / Adaptador** | Interfaz que define la aplicación / clase que la implementa con tecnología concreta |
| **Agregado** | Grupo de objetos con una raíz (`Sale`) que protege sus reglas |
| **Objeto de valor** | Objeto definido por sus valores, inmutable y siempre válido (`Cedula`, `CashTender`) |
| **Idempotencia** | Repetir la misma operación produce el mismo resultado una sola vez |
| **JWT** | Token firmado que identifica al cajero en cada petición |
| **ProblemDetails** | Formato estándar de errores HTTP (RFC 9110/7807) |
| **user-secrets** | Almacén de secretos de .NET fuera de la carpeta del proyecto |
| **Doble de prueba** | Objeto falso que reemplaza a uno real en una prueba (mock/stub) |
| **Docker** | Ejecuta Odoo y PostgreSQL en contenedores sin instalarlos en Windows |

---

## 14. Plan de estudio sugerido

| Día | Qué estudiar | Cómo comprobar que lo entendieron |
|---|---|---|
| 1 | Secciones 1, 2 y 5 (proyecto, enunciado, Odoo) | Explicar en 2 minutos qué hace el sistema y qué hace Odoo |
| 2 | Secciones 3 y 4 (arquitectura y recorrido) | Contar el recorrido de una venta abriendo cada archivo |
| 3 | Sección 6 + `decisiones-diseno.md` | Cada uno explica 2 decisiones con problema, alternativas y consecuencias |
| 4 | Secciones 7–10 | Correr las pruebas y explicar P05, P06 y P10 |
| 5 | Secciones 11–12 | Simulacro: uno pregunta, otro responde; hacer en vivo los cambios 1, 2 y 6 |

**Reparto sugerido** (todos deben saber todo, pero cada uno profundiza en algo):
- Integrante A: dominio y Strategy (D1, D4, D6).
- Integrante B: integración con Odoo e idempotencia (D2, D3, D8).
- Integrante C: frontend, Clean Architecture y validaciones (D5, D7).
- Todos: el recorrido de una venta (sección 4) y los cambios de requisito (sección 12).
