# Diagrama UML de la base de datos

Diagrama de clases UML del esquema propuesto en [01-data-model.md](01-data-model.md). Cada clase es una tabla; los atributos llevan tipo PostgreSQL y marca `«PK»`, `«FK»`, `«UK»` (único). Las multiplicidades están en los extremos de cada asociación. Versión renderizada: [database-uml.svg](database-uml.svg).

> Nota Mermaid: `char3`, `char4`, `char7` y `numeric14_2` representan `char(3)`, `char(4)`, `char(7)` y `numeric(14,2)`; Mermaid interpreta los paréntesis como métodos.

Convenciones UML usadas:
- **Composición** (rombo relleno): el hijo no existe sin el padre y se borra con él (`bill_participant`, `codi_qr`).
- **Agregación** (rombo vacío): el hijo referencia al padre pero sobrevive a su anonimización (`payment`, `refresh_token`, `privacy_consent`).
- **Asociación** simple: referencia por FK sin ciclo de vida compartido.
- Las clases con `«ephemeral»` se borran físicamente; `«immutable»` nunca se actualizan; `«inbox»`/`«outbox»` son tablas de mensajería; `«catalog»` es dato de referencia.

```mermaid
classDiagram
    direction TB

    class currency {
        <<catalog>>
        code : char3 «PK»
        name : text
        minor_unit : smallint
        is_active : boolean
    }

    class app_user {
        id : uuid «PK»
        email_encrypted : bytea
        email_hash : bytea «UK»
        phone_encrypted : bytea
        display_name : text
        password_hash : text
        encryption_key_version : smallint
        status : text
        created_at : timestamptz
        updated_at : timestamptz
        deleted_at : timestamptz
    }

    class privacy_consent {
        id : uuid «PK»
        user_id : uuid «FK»
        notice_version : text
        accepted_at : timestamptz
        ip_hash : bytea
    }

    class refresh_token {
        id : uuid «PK»
        user_id : uuid «FK»
        token_hash : bytea «UK»
        issued_at : timestamptz
        expires_at : timestamptz
        revoked_at : timestamptz
        replaced_by_id : uuid «FK self»
    }

    class user_bank_account {
        id : uuid «PK»
        user_id : uuid «FK»
        pey_bank_account_id : text «UK»
        alias : text
        clabe_last4 : char4
        codi_status : text
        created_at : timestamptz
        updated_at : timestamptz
    }

    class bill {
        id : uuid «PK»
        owner_user_id : uuid «FK»
        receiving_bank_account_id : uuid «FK»
        currency_code : char3 «FK»
        title : text
        total_amount : numeric14_2
        split_method : text
        status : text
        created_at : timestamptz
        updated_at : timestamptz
        settled_at : timestamptz
        cancelled_at : timestamptz
    }

    class bill_participant {
        id : uuid «PK»
        bill_id : uuid «FK»
        user_id : uuid «FK nullable»
        display_name : text
        position : smallint
        share_amount : numeric14_2
        is_owner : boolean
        status : text
        paid_at : timestamptz
        created_at : timestamptz
        updated_at : timestamptz
    }

    class codi_qr {
        <<ephemeral>>
        id : uuid «PK»
        participant_id : uuid «FK, UK»
        pey_codi_qr_id : text «UK»
        qr_string_encrypted : bytea
        encryption_key_version : smallint
        amount_requested : numeric14_2
        pey_num_ref : char7
        created_at : timestamptz
        expires_at : timestamptz
    }

    class payment {
        <<immutable>>
        id : uuid «PK»
        participant_id : uuid «FK»
        webhook_event_id : uuid «FK»
        pey_transaction_id : text «UK»
        pey_codi_qr_id : text
        currency_code : char3 «FK»
        amount : numeric14_2
        paid_at : timestamptz
        pey_num_ref : text
        pey_tracking_key : text
        payer_bank_code : integer
        payer_account_type : smallint
        is_unexpected : boolean
        created_at : timestamptz
    }

    class webhook_event {
        <<inbox>>
        id : uuid «PK»
        provider : text
        provider_message_id : text «UK»
        event_type : text
        payload_encrypted : bytea
        encryption_key_version : smallint
        provider_timestamp : timestamptz
        received_at : timestamptz
        status : text
        attempts : smallint
        processed_at : timestamptz
        last_error : text
    }

    class outbox_event {
        <<outbox>>
        id : uuid «PK»
        aggregate_type : text
        aggregate_id : uuid
        event_type : text
        payload : jsonb
        occurred_at : timestamptz
        published_at : timestamptz
        attempts : smallint
        last_error : text
    }

    class audit_log {
        <<append-only>>
        id : uuid «PK»
        actor_user_id : uuid «FK nullable»
        action : text
        entity_type : text
        entity_id : uuid
        ip_hash : bytea
        details : jsonb
        occurred_at : timestamptz
    }

    class data_subject_request {
        id : uuid «PK»
        user_id : uuid «FK»
        request_type : text
        status : text
        requested_at : timestamptz
        due_at : timestamptz
        resolved_at : timestamptz
        resolution_notes : text
    }

    currency "1" <-- "0..*" bill : currency_code
    currency "1" <-- "0..*" payment : currency_code

    app_user "1" <-- "0..*" bill : owner_user_id
    app_user "0..1" <-- "0..*" bill_participant : user_id
    app_user "1" *-- "0..*" user_bank_account : user_id
    app_user "1" o-- "0..*" refresh_token : user_id
    app_user "1" o-- "0..*" privacy_consent : user_id
    app_user "1" o-- "0..*" data_subject_request : user_id
    app_user "0..1" <-- "0..*" audit_log : actor_user_id
    refresh_token "0..1" <-- "0..1" refresh_token : replaced_by_id

    user_bank_account "1" <-- "0..*" bill : receiving_bank_account_id

    bill "1" *-- "1..*" bill_participant : bill_id
    bill_participant "1" *-- "0..1" codi_qr : participant_id
    bill_participant "1" o-- "0..*" payment : participant_id

    webhook_event "1" <-- "0..*" payment : webhook_event_id

    note for bill "SUM(share_amount) = total_amount · OPEN | SETTLED | CANCELLED"
    note for bill_participant "PENDING | PAID | WAIVED · UNIQUE(bill_id, position)"
    note for codi_qr "deleted on payment or expiry (20 min) · one live QR per participant"
    note for payment "UNIQUE(pey_transaction_id) = idempotency key"
    note for webhook_event "UNIQUE(provider_message_id) = webhook-id header"
```

## Lectura rápida de las relaciones

| Relación | Multiplicidad | Tipo | Significado |
|---|---|---|---|
| `app_user` → `bill` | 1 : 0..* | asociación | Un usuario es dueño de muchas cuentas; toda cuenta tiene exactamente un dueño. |
| `bill` ◆ `bill_participant` | 1 : 1..* | composición | Una cuenta se divide en al menos un participante; los participantes se borran con la cuenta. |
| `app_user` → `bill_participant` | 0..1 : 0..* | asociación | Un participante puede ser usuario registrado o invitado (sin usuario). |
| `bill_participant` ◆ `codi_qr` | 1 : 0..1 | composición | A lo más un QR vivo por cobro. |
| `bill_participant` ◇ `payment` | 1 : 0..* | agregación | Normalmente 0 o 1 pago; más de uno solo si `is_unexpected`. |
| `webhook_event` → `payment` | 1 : 0..* | asociación | Cada pago nace de un evento del inbox; un evento puede no producir pago (duplicado). |
| `user_bank_account` → `bill` | 1 : 0..* | asociación | La cuenta receptora del dinero se fija al crear la cuenta. |
| `currency` → `bill`, `payment` | 1 : 0..* | asociación | Divisa de la cuenta y divisa reportada por Pey (hoy siempre MXN). |
| `refresh_token` → `refresh_token` | 0..1 : 0..1 | auto-asociación | Cadena de rotación para detectar reuso. |

`outbox_event` no tiene FK a propósito: referencia agregados por `(aggregate_type, aggregate_id)` para que el relay no dependa del esquema de negocio y los eventos sobrevivan a borrados físicos (`codi_qr`).

## Alternativa que el equipo está evaluando

Si deciden separar el cobro del participante, el cambio es local: aparece `charge { id, participant_id «FK», amount, status, created_at }` entre `bill_participant` y `codi_qr`/`payment` (`bill_participant 1 — 0..* charge`, `charge 1 — 0..1 codi_qr`, `charge 1 — 0..* payment`), y `share_amount`/`status`/`paid_at` salen de `bill_participant`. El resto del diagrama no se mueve.
