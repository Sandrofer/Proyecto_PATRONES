# Diagramas

Los diagramas están en [Mermaid](https://mermaid.js.org/): GitHub y VS Code (extensión
*Markdown Preview Mermaid Support*) los dibujan solos. Para la exposición se pueden exportar
a imagen en <https://mermaid.live> (pegar el bloque y descargar PNG/SVG).

## 1. Despliegue (presentación con dos computadoras)

```mermaid
flowchart LR
    subgraph PC2["Computadora 2 · Frontend"]
        B["Navegador<br/>Blazor WebAssembly<br/>:5200"]
    end
    subgraph PC1["Computadora 1 · Backend"]
        API["API ASP.NET Core<br/>:5100"]
        subgraph Docker
            ODOO["Odoo 20 Community<br/>:8069"]
            PG[("PostgreSQL")]
        end
    end
    B -- "HTTP + JSON<br/>JWT del cajero" --> API
    API -- "JSON-2 /json/2/…<br/>API key (user-secrets)" --> ODOO
    API -. "login del cajero<br/>/web/session/authenticate" .-> ODOO
    ODOO --> PG
```

- El navegador **nunca** habla con Odoo ni conoce la API key (RNF-09).
- La API **nunca** toca PostgreSQL de Odoo: todo pasa por la API de Odoo.

## 2. Capas (Clean Architecture)

Las flechas son referencias entre proyectos: siempre apuntan hacia el dominio.

```mermaid
flowchart TB
    subgraph Frontend
        direction TB
        FW["Pos.Frontend.Web<br/>páginas Blazor"] --> FI["Pos.Frontend.Infrastructure<br/>HttpPosApi"]
        FW --> FA
        FI --> FA["Pos.Frontend.Application<br/>CheckoutUseCase · LoginUseCase · IPosApi"]
        FA --> FD["Pos.Frontend.Domain<br/>Cart · Cedula · CashTender"]
    end
    subgraph Backend
        direction TB
        AP["Pos.Api<br/>endpoints · JWT · errores"] --> IN["Pos.Infrastructure<br/>OdooPosGateway · OdooOperatorDirectory<br/>OdooJson2Client · InMemoryIdempotencyStore"]
        AP --> AA
        IN --> AA["Pos.Application<br/>interactors · SaleBuilder<br/>puertos IPosGateway, IOperatorDirectory…"]
        AA --> DO["Pos.Domain<br/>Sale · IDiscountPolicy · Customer<br/>Cedula · CashTender"]
    end
    SH["Pos.Shared<br/>contratos (DTO) · InputRules"]
    FA --> SH
    AA --> SH
    FI -. "HTTP" .-> AP
```

`ArchitectureTests` comprueba que `Pos.Domain` no depende de nada y que `Pos.Application`
no conoce Infrastructure ni ASP.NET.

## 3. Strategy: políticas de descuento (D1)

```mermaid
classDiagram
    direction LR
    class IDiscountPolicy {
        <<interface>>
        +Code string
        +Description string
        +RequiresPercent bool
        +MaxPercent decimal?
        +MinimumAmount decimal? = null
        +RequiresIdentifiedCustomer bool = false
        +Evaluate(DiscountContext) DiscountDecision
    }
    class NoDiscountPolicy
    class ManualPercentageDiscountPolicy {
        máximo 10 %
    }
    class StudentDiscountPolicy {
        10 % · requiere cédula
    }
    class VolumeDiscountPolicy {
        5 % · compra mínima $100
    }
    class DiscountPolicyCatalog {
        +Find(code) IDiscountPolicy?
        +All
    }
    class Sale {
        +ApplyDiscount(policy, percent)
    }
    IDiscountPolicy <|.. NoDiscountPolicy
    IDiscountPolicy <|.. ManualPercentageDiscountPolicy
    IDiscountPolicy <|.. StudentDiscountPolicy
    IDiscountPolicy <|.. VolumeDiscountPolicy
    DiscountPolicyCatalog o-- "*" IDiscountPolicy
    Sale ..> IDiscountPolicy : usa
```

Agregar un descuento = una clase nueva y una línea en `DependencyContainer`. Sus condiciones
(`MinimumAmount`, `RequiresIdentifiedCustomer`) viajan en `GET /api/discount-policies` y la
pantalla las valida sin cambios.

## 4. Secuencia del cobro (RF-11, RF-16/17, RN-07)

```mermaid
sequenceDiagram
    autonumber
    actor C as Cajero
    participant UI as Navegador<br/>(CheckoutUseCase)
    participant API as API<br/>(RegisterSaleInteractor)
    participant ID as IdempotencyStore
    participant O as Odoo 20

    C->>UI: Cobrar (recibido $5,00)
    UI->>UI: valida stock, cliente, descuento, efectivo
    Note over UI: si algo falla: aviso y botón bloqueado,<br/>no se llama a la API
    UI->>API: POST /api/sales<br/>Idempotency-Key: GUID
    API->>ID: reservar clave
    alt la clave ya se está procesando
        API-->>UI: 409 en curso
    end
    API->>O: ¿existe pos.order con uuid = GUID?
    alt ya existe
        API-->>UI: 200 ya registrada (no se duplica)
    end
    API->>O: sesión POS abierta y método de pago
    API->>O: productos, precios, impuestos, stock
    API->>API: Sale (agregado) + descuento (Strategy)<br/>+ CashTender (vuelto $2,93)
    API->>O: pos.order.sync_from_ui<br/>pago $5,00 · amount_return −2,93
    alt Odoo responde
        O-->>API: ok
        API->>O: leer la orden guardada (RF-13)
        API-->>UI: 201 · Caja 1 - 000015 · vuelto $2,93
        UI-->>C: recibo y vuelto
    else timeout / Odoo caído
        API->>O: volver a buscar por uuid
        API-->>UI: 202 incierto
        UI-->>C: "Reintentar" (misma clave: no duplica)
    end
```

## 5. Estados de la venta (D4)

```mermaid
stateDiagram-v2
    state "En preparación" as Prep
    state "Confirmada" as Conf
    [*] --> Prep : new Sale(referencia)
    Prep --> Prep : AddProduct / AssignCustomer / ApplyDiscount
    Prep --> Conf : Confirm()
    Conf --> [*] : enviada a Odoo
    note right of Conf : ya no se puede editar (EnsureInPreparation)
```
