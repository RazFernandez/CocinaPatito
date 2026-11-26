# Entorno de desarrollo con Vagrant + Docker

Vagrant usa **Docker como proveedor**: en lugar de una máquina virtual completa, crea **un solo
contenedor** Ubuntu 24.04 y lo prepara con `bootstrap.sh`. Dentro quedan el SDK de .NET 10,
PostgreSQL 18, la base de datos con su esquema y las librerías NuGet del proyecto.

| Archivo | Qué hace |
|---|---|
| [`../Vagrantfile`](../Vagrantfile) | Define el contenedor, los puertos y qué script corre |
| [`Dockerfile.dev`](Dockerfile.dev) | Imagen mínima: Ubuntu, servidor SSH y usuario `vagrant` para que Vagrant pueda entrar |
| [`bootstrap.sh`](bootstrap.sh) | Instalación paso a paso: paquetes, .NET 10, PostgreSQL 18, base de datos, NuGet |
| [`db.env.example`](db.env.example) | Plantilla de usuario y contraseñas de la base de datos |
| `db.env` | **Tu copia con los valores reales. Git la ignora.** |

## Requisitos en Windows

- Docker Desktop encendido, con contenedores Linux.
- Vagrant 2.4 o superior.

## 1. Usuario y contraseñas: dónde se editan

**Solo en `provision/db.env`.** Ningún otro archivo lleva credenciales.

```bash
copy provision\db.env.example provision\db.env
```

Abre `provision/db.env` y cambia estas líneas:

| Variable | Qué es | Valor de ejemplo |
|---|---|---|
| `DB_NAME` | Nombre de la base de datos | `splitbill` |
| `DB_OWNER_USER` | **Usuario** dueño de la base y de las tablas; corre las migraciones | `splitbill_owner` |
| `DB_OWNER_PASSWORD` | **Contraseña** de ese usuario | mínimo 12 caracteres |
| `DB_APP_PASSWORD` | **Contraseña** de `splitbill_app`, el usuario con permisos mínimos que usará la API | mínimo 12 caracteres |

El nombre del usuario de la API, `splitbill_app`, es fijo porque lo define la migración
`SQL/0002_app_role.sql`. Solo su contraseña se configura aquí.

`bootstrap.sh` se niega a continuar si `db.env` no existe, si las contraseñas siguen con el valor
`change-me…` o si tienen menos de 12 caracteres.

## 2. Levantar el entorno

```bash
vagrant up --provider=docker
```

La primera vez tarda varios minutos: construye la imagen y corre `bootstrap.sh`.

## 3. Uso diario

```bash
vagrant ssh
```

Dentro del contenedor el proyecto está en `/vagrant`:

```bash
cd /vagrant && dotnet test
```

| Comando | Efecto |
|---|---|
| `vagrant halt` | Detiene el contenedor |
| `vagrant up` | Lo vuelve a encender; PostgreSQL arranca solo |
| `vagrant provision` | Vuelve a correr `bootstrap.sh`; solo hace lo que falta |
| `vagrant destroy -f` | Borra el contenedor **y la base de datos que tiene dentro** |

## Cambiar una contraseña después

Edita `provision/db.env` y ejecuta:

```bash
vagrant provision
```

Las contraseñas se actualizan en PostgreSQL sin perder datos. Si cambias `DB_NAME` o
`DB_OWNER_USER` después de crearlos, se crean una base o un usuario **nuevos** y los anteriores
quedan; para empezar limpio usa `vagrant destroy -f` y `vagrant up`.

## Conectarse a la base desde Windows

Con DBeaver, pgAdmin o psql:

| Campo | Valor |
|---|---|
| Host | `127.0.0.1` |
| Puerto | `5433` |
| Base de datos | el `DB_NAME` de `db.env` |
| Usuario | `DB_OWNER_USER` para administrar, `splitbill_app` para probar como la API |
| Contraseña | la correspondiente de `db.env` |

Los puertos se publican solo en `127.0.0.1`: ninguna otra computadora de la red puede conectarse.
Se usa el **5433** en Windows porque el 5432 suele estar ocupado por un PostgreSQL instalado
localmente. Dentro del contenedor PostgreSQL sigue en el 5432.

## Notas de seguridad

- El usuario `vagrant` con contraseña `vagrant` es el acceso estándar de Vagrant, solo para
  desarrollo. Vagrant lo cambia por una llave propia en el primer arranque, y el puerto SSH solo
  escucha en `127.0.0.1`.
- Las contraseñas de `db.env` llegan a PostgreSQL como variables de psql: nunca aparecen en una
  línea de comandos ni se concatenan dentro del SQL.
- Los archivos `bin/` y `obj/` del contenedor se guardan en `/home/vagrant/.artifacts`, no en la
  carpeta compartida, para no chocar con las compilaciones hechas en Windows.
