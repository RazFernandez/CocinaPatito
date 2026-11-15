# Diagrama conceptual para el pitch

Versión abstracta del modelo (sin tablas, tipos ni columnas) pensada para explicar la idea ante los jueces en menos de un minuto. Renderizado: [pitch-diagram.png](pitch-diagram.png) · [pitch-diagram.svg](pitch-diagram.svg).
La versión técnica completa está en [04-database-uml.md](04-database-uml.md).

```mermaid
flowchart TB
    classDef person fill:#FDE68A,stroke:#B45309,color:#1F1300,stroke-width:1.5px
    classDef core fill:#DBEAFE,stroke:#1D4ED8,color:#0B1B3F,stroke-width:1.5px
    classDef ephemeral fill:#FFFFFF,stroke:#1D4ED8,color:#0B1B3F,stroke-dasharray:6 4,stroke-width:1.5px
    classDef money fill:#DCFCE7,stroke:#15803D,color:#052E16,stroke-width:1.5px
    classDef pey fill:#F3E8FF,stroke:#7E22CE,color:#2E1065,stroke-width:1.5px
    classDef trust fill:#F1F5F9,stroke:#64748B,color:#0F172A,stroke-width:1px

    subgraph MESA["🍽️  La cuenta"]
        direction TB
        H(["Anfitrión<br/>ya pagó la cuenta completa"]):::person
        B["Cuenta<br/>500 MXN · partes iguales o montos propios"]:::core
        P1["Participante<br/>debe 166.67"]:::core
        P2["Participante<br/>debe 166.67"]:::core
        P3["Participante<br/>debe 166.66"]:::core
        H -- crea --> B
        B -- se divide en --> P1 & P2 & P3
    end

    subgraph COBRO["📱  Cobro CoDi vía Pey"]
        direction TB
        QR["QR CoDi<br/>monto exacto · vive 20 min · se destruye al usarse"]:::ephemeral
        BANK(["App bancaria del participante"]):::person
        PEY[["Pey · API CoDi"]]:::pey
        PAY["Pago confirmado<br/>registrado una sola vez"]:::money
        P1 -- genera --> QR
        QR -- se escanea en --> BANK
        BANK -- transfiere por CoDi --> PEY
        PEY -- webhook firmado --> PAY
        PAY -- marca pagado a --> P1
    end

    subgraph VIVO["📊  Pantalla en vivo"]
        S["Quién ya aportó · cuánto falta · sin sumar comprobantes a mano"]:::money
    end

    PAY -. actualiza .-> S
    B -. se liquida cuando todos pagan .-> S

    subgraph CONF["🔒  Confianza"]
        direction LR
        T1["Datos personales mínimos y cifrados"]:::trust
        T2["Derechos ARCO · aviso de privacidad"]:::trust
        T3["Cero cobros dobles · QR efímero"]:::trust
    end

    MESA ~~~ CONF
```

## Cómo narrarlo (30 segundos)

1. **La cuenta**: alguien ya pagó; en la app crea la cuenta y la divide. El sistema cuadra los centavos solo.
2. **El cobro**: cada participante recibe su propio QR CoDi con su monto exacto. Lo escanea con la app de su banco, sin tarjeta, sin efectivo, sin comisión.
3. **Pey**: Pey genera el QR ante Banxico y nos avisa con un webhook firmado cuando el dinero llegó. Verificamos con Pey y marcamos pagado. El QR desaparece: no puede cobrarse dos veces.
4. **La pantalla**: todos ven en vivo quién aportó y cuánto falta. Cuando el último paga, la cuenta se liquida sola.
