# Notas de la API de Pey (solo lo que usamos: CoDi QR + webhooks)

Fuente: https://docs.pey.mx (versión de la API `2026-10-20`) y `https://docs.pey.mx/openapi.json`. Consultado 2026-11-14.

## Generalidades
- Base URL: `https://api.pey.mx` (solo producción; **sandbox "próximamente"**; recomiendan probar con $0.01 MXN).
- Auth: `Authorization: Bearer <API key>` (esquema `dev-PublicApiLambdaAuthorizer`, HTTP bearer).
- API key se crea en `portal.mipey.mx` → Desarrollador → "Crear API Key", ligada a una cuenta de pago `codi` (y/o `stripe`).
- **Una API key = un webhook** (URL + secreto `whsec_…`, rotable desde el portal). Logs de entrega en Desarrollador → ⚙ → "Webhook Logs".
- Firma: **Standard Webhooks** (headers `webhook-id`, `webhook-timestamp`, `webhook-signature`; HMAC-SHA256 sobre `"{id}.{timestamp}.{body}"`). Hay SDK oficial C# (`StandardWebhooks` en NuGet).
- Montos: `number` en MXN con decimales (ej. `150`, `129.5`). Fechas: epoch segundos.

## `POST /codi/qr` — crear QR
Request (`application/json`):

| Campo | Tipo | Reglas |
|---|---|---|
| `amount` | number | requerido, `>= 0`; `0` = el pagador captura el monto (no lo usamos). |
| `concept` | string | se muestra al pagador. |
| `expiration` | integer | segundos; `0` = sin expiración (default); si se envía, mínimo `360`. **Nosotros: `1200`.** |
| `numRef` | string | `^[0-9]{1,7}$`, default `"0"`. |
| `generateImage` | boolean | default `false`; `true` devuelve `qrImage` base64 PNG. **No lo usamos.** |
| `bankAccountId` | string (ULID) | cuenta con `codiStatus: ACTIVE`; a dónde llega el dinero. |
| `metadata` | object | ≤ 50 pares, keys ≤ 40 chars, valores string/number/boolean/null ≤ 500 chars. Vuelve como `externalMetadata` (todo string). |

Response `201`: `{ "id": "<IDC Banxico>", "qrString": "<JSON a codificar en QR>", "qrImage": null }`.
Errores `400`: `INVALID_REQUEST`, `PEY_CONSISTENCY_ERROR`, `INVALID_STATE`, `INVALID_AMOUNT`, `INVALID_PARAMETER`. `503`: `SERVICE_UNAVAILABLE`, `FLAGS_UNAVAILABLE`. `500`: `INTERNAL_SERVER_ERROR`.

## `GET /transaction/codi/qr/{id}?nextToken=` — transacciones de un QR
`id` = IDC. Response `200`: `{ items: [{ id (ULID), status: PENDING|PAID, amount, amountBreakdown{base,tip,fee}, currency (ISO 4217), paymentMethod: CODI, description, applicationFee, dateCreated, datePaid?, metadata }], nextToken }` (20 por página, más recientes primero). `404` si el QR no es de nuestra API key.

Lo usamos como **verificación independiente** antes de marcar un cobro como pagado.

## Webhook `codi.qr.payment_received`
```json
{
  "type": "codi.qr.payment_received",
  "transactionId": "01M3QR8H0RH0XBCKYYD242YXNV",
  "codiQrId": "35a13c183a",
  "apiKeyId": "01M3QMMR6X9CBVDY1SF3G8QWNW",
  "bankAccountId": "01JBAVWE4NS5TGXHWGMX3KH23Q",
  "amount": 150,
  "concept": "(dev) Order 345",
  "numRef": "34567",
  "datePaid": 1790724818,
  "metadata": {
    "clabe": "000000000000000000",
    "bankCode": 40012,
    "accountType": 40,
    "description": "(dev) Order 345",
    "trackingKey": "0192191042",
    "externalMetadata": { "billId": "…", "participantId": "…" }
  }
}
```
Mapeo a nuestra BD: `transactionId → payment.pey_transaction_id` (UNIQUE), `codiQrId → codi_qr.pey_codi_qr_id` / `payment.pey_codi_qr_id`, `amount → payment.amount`, `datePaid → payment.paid_at`, `numRef → payment.pey_num_ref`, `metadata.trackingKey → payment.pey_tracking_key`, `metadata.bankCode/accountType → payer_bank_code/payer_account_type`. **`metadata.clabe` no se persiste** fuera del payload cifrado del inbox.

## Fuera de alcance (decisión del equipo)
`POST /codi/pn` (push notification a teléfono), Payment Links, Stripe. Registro de cuentas bancarias (`POST /bank-account`, `GET /bank-account/{id}`, webhook `bank-account.updated`) se usa solo para obtener el `bankAccountId` del dueño de la cuenta.

## Preguntas abiertas para Pey / el equipo
1. ¿Reintentos del webhook: cuántos y con qué back-off? (no documentado; asumimos que reintentan ante no-2xx).
2. ¿Puede un mismo QR recibir dos pagos (dos personas escanean)? Asumimos sí → `payment.is_unexpected`.
3. ¿El `qrString` cambia si se vuelve a consultar? Asumimos inmutable por IDC.
4. ¿Hay límite de QRs por minuto por API key? Afecta el rate limit que pongamos en `POST …/codi-qr`.
