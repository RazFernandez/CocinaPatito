# Guía para desarrolladores

Backend puro (.NET 10, Dapper, PostgreSQL 18) que divide una cuenta entre varias personas y
cobra a cada participante con un QR CoDi generado a través de la API de Pey.

## Documentación de diseño

| Documento | Contenido |
|---|---|
| [01-data-model.md](01-data-model.md) | Modelo de datos, diagrama ER, decisiones, transacciones, cifrado, retención, 3FN |
| [02-architecture.md](02-architecture.md) | Arquitectura hexagonal + event driven, seguridad (OWASP 2025), JWT, webhooks, Docker, TDD, LFPDPPP |
| [03-pey-api-notes.md](03-pey-api-notes.md) | Resumen de la API de Pey (CoDi QR + webhooks) y mapeo a nuestras tablas |
| [04-database-uml.md](04-database-uml.md) | Diagrama UML de clases de la base de datos ([PNG](database-uml.png), [SVG](database-uml.svg)) |
| [05-pitch-diagram.md](05-pitch-diagram.md) | Diagrama conceptual para el pitch ([PNG](pitch-diagram.png), [SVG](pitch-diagram.svg)) |

## Estructura

```
SQL/                         Scripts DDL versionados (0001_initial_schema.sql, 0002_app_role.sql)
src/SplitBill.Domain/        Dominio puro: Money, SplitCalculator, Bill, CodiQr, eventos
tests/SplitBill.Domain.Tests Pruebas xunit.v3 + Shouldly (TDD)
Directory.Build.props        Reglas de compilación compartidas (nullable, warnings as errors, XML docs)
Directory.Packages.props     Versiones de paquetes centralizadas y fijas
```

Capas pendientes (siguiente bloque): `SplitBill.Application`, `SplitBill.Infrastructure`,
`SplitBill.Api`, `SplitBill.Worker`, Dockerfile y docker-compose.

## Requisitos

- .NET SDK 10.0.x
- Docker (para PostgreSQL 18 local y pruebas de integración)

## Pruebas

```bash
dotnet test
```

## Base de datos

Aplicar el esquema a una instancia local de PostgreSQL 18:

```bash
docker run -d --name splitbill-pg -e POSTGRES_PASSWORD=dev -e POSTGRES_DB=splitbill -p 5432:5432 postgres:18
```

```bash
docker exec -i splitbill-pg psql -U postgres -d splitbill -v ON_ERROR_STOP=1 < SQL/0001_initial_schema.sql
```

```bash
docker exec -i splitbill-pg psql -U postgres -d splitbill -v ON_ERROR_STOP=1 < SQL/0002_app_role.sql
```

La contraseña del rol `splitbill_app` se asigna fuera del repositorio (`ALTER ROLE … PASSWORD`).

## Reglas del proyecto

- Identificadores, comentarios y documentación XML en inglés.
- Prohibido el SQL dinámico: solo sentencias estáticas parametrizadas.
- Dinero siempre en `decimal` / `numeric(14,2)` con `currency_code`.
- Ningún secreto en el repositorio: variables de entorno o gestor de secretos.
- Cada método público lleva `<summary>`; los centrales incluyen `<example>`.
