# Registro de decisiones de diseño

Categorías usadas: **Patrón GoF**, **Patrón empresarial / de integración**,
**Enfoque arquitectónico** (Clean Architecture), **Principio** (SOLID), **Práctica** (Clean Code).

---

## D1. Políticas de descuento intercambiables — *Strategy* (GoF)

**Problema.** RF-06/07/08: el negocio tiene varios descuentos (manual con tope, estudiante,
por volumen) y pedirá otros. Cada uno tiene su propia regla de autorización.

**Alternativas.**
1. `switch` sobre un `enum DiscountType` dentro de la venta: simple, pero cada descuento nuevo
   modifica `Sale` y su prueba (viola Open/Closed).
2. Reglas en base de datos/configuración: flexible, pero sobredimensionado para 3–4 reglas con lógica distinta.
3. **Strategy**: una interfaz `IDiscountPolicy` y una clase por regla.

**Elección.** Strategy (GoF) + Open/Closed y DIP (SOLID). `Sale.ApplyDiscount(policy, %)` solo
acepta decisiones aprobadas; `DiscountPolicyCatalog` recibe todas las políticas registradas en DI.

**Consecuencias.** + Agregar una política = 1 clase + 1 línea de registro. + Cada regla se prueba
aislada. − Más clases; para una sola regla fija no aportaría valor (por eso no hay jerarquías extra).

**Código.** `Pos.Domain/Discounts/DiscountPolicies.cs`, `Pos.Application/Discounts/DiscountPolicyCatalog.cs`,
registro en `Pos.Infrastructure/DependencyContainer.cs`.

**Prueba.** `DiscountExtensibilityTests.P10_A_new_policy_works_without_changing_existing_components`
(agrega `EmployeeDiscountPolicy` sin modificar nada más), `SaleTests` (rechazo con mensaje claro).

---

## D2. Aislar Odoo detrás de un puerto — *Adapter / Gateway* (empresarial) + *Ports & Adapters*

**Problema.** Odoo es un sistema externo con un formato propio (JSON-2, dominios en notación
polaca, comandos `(0, 0, {...})`, `false` como vacío, binarios como `{content}`). Si ese conocimiento
se mezcla con las reglas, el código es intesteable y cualquier cambio de Odoo rompe todo.

**Alternativas.**
1. Llamar a Odoo desde los endpoints: rápido, pero mezcla HTTP, reglas y formato de Odoo.
2. Acceder a PostgreSQL de Odoo: **prohibido** por el enunciado y salta las reglas de Odoo.
3. **Puerto `IPosGateway` en Application + adaptador `OdooPosGateway` en Infrastructure.**

**Elección.** Gateway/Adapter (Fowler, GoF Adapter) con inversión de dependencias (DIP):
Application define qué necesita; Infrastructure lo traduce a Odoo. `OdooJson2Client` encapsula
el transporte y convierte fallos en excepciones de Application.

**Consecuencias.** + Casos de uso probados con dobles (NSubstitute) sin Odoo. + Cambiar de
mecanismo (p. ej. XML-RPC o un módulo propio) solo toca el adaptador. − Un mapeo más que mantener.

**Hallazgos reales al integrar** (quedan encapsulados en el adaptador):
- JSON-2 ignora cuerpos *chunked*: se envía el JSON con `Content-Length` (`OdooJson2Client`).
- En Odoo 20 la API key debe tener scope `rpc` (`setup_pos.py`).
- Odoo recalcula el total y exige pago exacto; la compañía usa `round_globally`, así que el
  dominio aplica el mismo modo de redondeo que lee de Odoo (`TaxRoundingMode`).

**Cajeros en Odoo (mismo principio).** Los cajeros son usuarios de Odoo: el caso de uso de login
solo depende del puerto `IOperatorDirectory`; el adaptador `OdooOperatorDirectory` valida la
contraseña con Odoo (`/web/session/authenticate`, sin guardar cookies) y comprueba el grupo
`point_of_sale.group_pos_user` con `res.users.has_group`. Así no se duplica la gestión de usuarios
(Odoo es el sistema central) y cada pedido queda con el `user_id` del cajero. Se cambió desde una
lista local de cajeros sin tocar Domain ni las reglas de venta: solo el adaptador.

**Código.** `Pos.Application/Ports/Ports.cs`, `Pos.Infrastructure/Odoo/OdooPosGateway.cs`, `OdooJson2Client.cs`, `OdooOperatorDirectory.cs`.

**Prueba.** `OdooPosGatewayTests` y `OdooOperatorDirectoryTests` (contrato con Odoo simulado), `LoginTests`, `Pos.IntegrationTests` (P09 real).

---

## D3. Evitar ventas duplicadas y éxitos falsos — *Idempotency Key* (patrón de integración)

**Problema.** RF-16/17, RN-07/08: doble clic, reintentos o cortes de red no deben crear dos
ventas, y si Odoo no responde no se puede afirmar que se cobró.

**Alternativas.**
1. Deshabilitar el botón: necesario pero insuficiente (reintentos, red, varias pestañas).
2. Tabla propia de ventas en .NET: duplica datos de Odoo y no sobrevive a fallos entre ambos.
3. **Clave de idempotencia por operación, usada como `uuid` de la orden en Odoo.**

**Elección.** El frontend genera un GUID por venta (`CheckoutUseCase`) y lo conserva hasta una
respuesta definitiva. La API: (a) bloquea en memoria la clave mientras se procesa (409 si llega
otra igual), (b) **antes de crear, busca en Odoo una orden con ese `uuid`**, (c) si hay timeout,
vuelve a consultar a Odoo y, si sigue sin saber, responde **202 "incierto"** en lugar de éxito.
Odoo 20 refuerza la garantía con la restricción `unique(uuid)` en `pos.order`.

**Consecuencias.** + Sin duplicados incluso tras reiniciar la API (la fuente de verdad es Odoo).
+ El cajero ve "incierto → reintentar" en vez de un éxito falso. − El almacén en memoria no se
comparte entre varias instancias de la API (suficiente para una caja; se podría pasar a Redis).

**Código.** `RegisterSaleInteractor.cs`, `InMemoryIdempotencyStore.cs`, `Endpoints.cs`
(cabecera `Idempotency-Key`), `frontend/.../UseCases.cs` (`CheckoutUseCase`).

**Prueba.** `RegisterSaleInteractorTests` P05/P06 (timeout, rechazo, repetición, concurrencia,
reinicio), `ApiErrorAndCheckoutTests` (misma clave tras fallo de red) y verificación manual
contra Odoo real (ver README).

---

## D4. Agregado `Sale` con estados — *Aggregate* (DDD) en vez del patrón *State*

**Problema.** RN-01, RN-02, RN-05, RN-06: una venta confirmada no se edita.

**Alternativas.** Patrón **State** (GoF) con clases `InPreparationState` / `ConfirmedState`, o un
**agregado con un enum** y una guarda.

**Elección.** Agregado con `SaleStatus` y `EnsureInPreparation()`. Con **dos estados y una sola
transición**, State agregaría clases sin beneficio; sería la opción correcta si aparecieran más
estados con comportamiento distinto (p. ej. "en espera", "anulada", "reembolsada").
*Ejemplo de cuándo un patrón no aporta valor.*

**Código.** `Pos.Domain/Sales/Sale.cs`. **Prueba.** `SaleTests.A_confirmed_sale_cannot_be_edited`.

---

## D5. Clean Architecture en ambas aplicaciones — *Enfoque arquitectónico*

- Backend: `Domain ← Application ← Infrastructure / Api`. El compilador lo impone mediante las
  referencias entre proyectos y `ArchitectureTests` lo verifica.
- Frontend: `Domain (Cart) ← Application (casos de uso, IPosApi) ← Infrastructure (HTTP) ← Web (Blazor)`.
  Los casos de uso no conocen Blazor ni `HttpClient` (RNF-02).
- Errores uniformes: excepciones de Application → `ProblemDetailsExceptionHandler` (RNF-08).

**Decisión consciente — sin Presenter:** a diferencia de NorthWind (input/output port + presenter),
los interactors devuelven DTOs directamente. Con una sola salida (JSON) el presenter solo copiaba
datos; los endpoints siguen sin lógica.

## Principios SOLID aplicados

| Principio | Dónde |
|---|---|
| S — Responsabilidad única | `SaleBuilder` construye, `RegisterSaleInteractor` coordina, `OdooPosGateway` traduce, `OdooJson2Client` transporta |
| O — Abierto/cerrado | Nuevas políticas de descuento sin modificar `Sale` ni los casos de uso (D1) |
| L — Sustitución de Liskov | Cualquier `IDiscountPolicy` o `IPosGateway` (real o doble) es intercambiable |
| I — Segregación de interfaces | Un input port por caso de uso; `IIdempotencyStore`, `ITokenIssuer`, `IOperatorDirectory` pequeños |
| D — Inversión de dependencias | Application define `IPosGateway`, `IPosApi`…; Infrastructure los implementa |
