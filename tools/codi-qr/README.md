# CoDi QR preview (herramienta extra)

Script independiente para **entender y probar** los QR que genera Pey. Toma la respuesta de
`POST /codi/qr`, explica qué trae cada campo y dibuja la imagen QR que se escanea con la app
del banco.

No forma parte del backend. En producción, nuestra API entrega el `qrString` y el frontend
dibuja el QR; este script solo reproduce ese paso para hacer pruebas.

> ⚠️ Pey no tiene sandbox: **cada QR es real y se puede pagar**. Las imágenes generadas y las
> respuestas reales quedan fuera de git (ver `.gitignore` de esta carpeta). No publiquen capturas.

## Uso

```bash
python -m venv .venv
```

```bash
.venv\Scripts\python -m pip install -r requirements.txt
```

Guarden la respuesta de Pey en `response.json` (ignorado por git) y ejecuten:

```bash
.venv\Scripts\python codi_qr_preview.py response.json
```

La imagen queda en `output/<IDC>.png`. Opciones: `--output ruta.png`, `--box-size 12`
(pixeles por cuadrito) y `--border 4` (margen blanco, mínimo 4 por norma QR).

## Qué pasa paso a paso

```
Pey responde                       →  {"id": "...", "qrString": "{\"TYP\":20, ...}"}
1. Se toma qrString TAL CUAL        →  495 bytes de texto (no se reinterpreta ni se reformatea)
2. Se codifica como QR              →  versión 17, 85 × 85 cuadritos, corrección de errores M
3. Se guarda como PNG               →  930 × 930 px con los valores por defecto
4. La app bancaria lo escanea       →  lee el texto, pide a Banxico la llave (SER) y descifra ENC
5. El banco muestra monto y concepto →  el pagador confirma y CoDi transfiere
6. Pey avisa a nuestro backend      →  webhook codi.qr.payment_received con codiQrId = id
```

El QR **no contiene el monto en claro**: el texto solo tiene identificadores y un bloque
cifrado (`ENC`, 256 bytes) más una verificación de integridad (`CRY`, 32 bytes). Por eso nuestro
backend guarda el monto por su cuenta: lo conoce porque fue quien lo pidió.

## Lo que comprobamos con la respuesta de prueba

| Prueba | Resultado |
|---|---|
| `id` coincide con `ic.IDC` dentro del QR | Sí |
| La imagen, leída con un decodificador independiente (OpenCV), devuelve exactamente el mismo `qrString` | Sí, 495 de 495 bytes |
| Tamaño mínimo en que se sigue leyendo | Alrededor de 320 px (unos 3.4 px por cuadrito) |

## Recomendaciones para el frontend

- **Usar el `qrString` exacto.** Si se convierte a objeto y se vuelve a escribir como texto,
  pueden cambiar espacios u orden y el QR deja de ser el que firmó Banxico.
- **Mostrarlo grande.** Este QR es denso: 85 × 85 cuadritos. En pantalla conviene al menos
  400 px de lado, con bordes nítidos (en CSS, `image-rendering: pixelated`) y margen blanco.
  La prueba de tamaño mínimo se hizo con una imagen perfecta; una cámara real necesita más.
- **Error correction M**, igual que el tutorial de Pey: tolera que se dañe alrededor del 15 %.
- **Alternativa:** pedir a Pey `generateImage: true` y mostrar el PNG en base64 que regresa.
