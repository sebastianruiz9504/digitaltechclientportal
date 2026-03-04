# Guía de diagnóstico en Postman para Contadores (Dataverse)

Esta guía valida, en orden, por qué la sección de **contadores** puede venir vacía.

## 0) Variables de entorno en Postman

Define estas variables en tu environment:

- `baseUrl` → URL de Dataverse (ej: `https://<tuorg>.crm2.dynamics.com`)
- `token` → bearer token que ya tienes
- `clienteId` → GUID de un cliente real (sin llaves)
- `equipoId` → GUID de un equipo real (sin llaves)
- `periodoInicio` → inicio de mes en UTC (ej: `2026-02-01T00:00:00Z`)
- `periodoFin` → inicio de mes siguiente en UTC (ej: `2026-03-01T00:00:00Z`)

## 1) Headers base (en todas las requests)

- `Authorization: Bearer {{token}}`
- `OData-MaxVersion: 4.0`
- `OData-Version: 4.0`
- `Accept: application/json`

## 2) Confirmar que existen clientes

**GET**

```http
{{baseUrl}}/api/data/v9.2/cr07a_clientes?$select=cr07a_clienteid,cr07a_nombre&$orderby=cr07a_nombre
```

Qué revisar:
- Que `value` tenga registros.
- Copia un `cr07a_clienteid` válido a `{{clienteId}}`.

## 3) Confirmar lookup de equipos -> cliente

**GET**

```http
{{baseUrl}}/api/data/v9.2/cr07a_equipos?$select=cr07a_equipoid,cr07a_nombredelequipo,_cr07a_cliente_value&$filter=_cr07a_cliente_value eq {{clienteId}}
```

Qué revisar:
- Que `value` tenga registros para ese cliente.
- Copia un `cr07a_equipoid` a `{{equipoId}}`.
- Guarda también `cr07a_nombredelequipo` (nos ayuda a validar si hay cruce por nombre/serial).

> Si esto no devuelve nada, el problema no está en contadores sino en la relación cliente-equipo.

## 4) Probar tabla principal de contadores (`cr07a_contadoreses`)

**GET**

```http
{{baseUrl}}/api/data/v9.2/cr07a_contadoreses?$select=cr07a_contador,cr07a_contadorescaner,cr07a_fechadetomadecontador,_cr07a_maquina_value&$filter=_cr07a_maquina_value eq {{equipoId}} and cr07a_fechadetomadecontador ge {{periodoInicio}} and cr07a_fechadetomadecontador lt {{periodoFin}}&$orderby=cr07a_fechadetomadecontador desc&$top=5
```

Qué revisar:
- Si da error de propiedad/segmento, revisamos esquema real.
- Si responde 200 con `value: []`, no hay lecturas en ese mes.
- Si trae datos, valida que `cr07a_fechadetomadecontador` y contadores tengan valores.

## 5) Probar nombre alterno de tabla (`cr07a_contadors`)

**GET**

```http
{{baseUrl}}/api/data/v9.2/cr07a_contadors?$select=cr07a_contador,cr07a_contadorescaner,cr07a_fechadetomadecontador,_cr07a_maquina_value&$filter=_cr07a_maquina_value eq {{equipoId}} and cr07a_fechadetomadecontador ge {{periodoInicio}} and cr07a_fechadetomadecontador lt {{periodoFin}}&$orderby=cr07a_fechadetomadecontador desc&$top=5
```

Qué revisar:
- Si este sí funciona y el anterior no, hay desalineación del entity set.

## 6) Probar tabla mensual alternativa (`cr07a_contadoresmensualesequipos`) por lookup

**GET**

```http
{{baseUrl}}/api/data/v9.2/cr07a_contadoresmensualesequipos?$select=cr07a_dt_contadorpaginas,cr07a_dt_paginasescaneadas,cr07a_dt_fechalectura,_cr07a_equipo_value,cr07a_equipo&$filter=_cr07a_equipo_value eq {{equipoId}} and cr07a_dt_fechalectura ge {{periodoInicio}} and cr07a_dt_fechalectura lt {{periodoFin}}&$orderby=cr07a_dt_fechalectura desc&$top=5
```

Qué revisar:
- Si esta tabla sí trae datos pero `cr07a_contadoreses` no, el origen real está aquí.

## 7) Probar tabla mensual alternativa por texto (`cr07a_equipo`)

Solo si en paso 3 guardaste `cr07a_nombredelequipo`.

**GET**

```http
{{baseUrl}}/api/data/v9.2/cr07a_contadoresmensualesequipos?$select=cr07a_dt_contadorpaginas,cr07a_dt_paginasescaneadas,cr07a_dt_fechalectura,_cr07a_equipo_value,cr07a_equipo&$filter=cr07a_equipo eq '<NOMBRE_O_SERIAL>' and cr07a_dt_fechalectura ge {{periodoInicio}} and cr07a_dt_fechalectura lt {{periodoFin}}&$orderby=cr07a_dt_fechalectura desc&$top=5
```

Qué revisar:
- Si por lookup falla pero por texto funciona, hay inconsistencia en el campo relacional.

## 8) Metadata para confirmar nombres reales (si algo falla por schema)

### 8.1 EntitySet real de “Contadores”

**GET**

```http
{{baseUrl}}/api/data/v9.2/EntityDefinitions?$select=LogicalName,SchemaName,EntitySetName&$filter=contains(LogicalName,'contador')
```

### 8.2 Campos reales de la tabla encontrada

(usa el logical name real que devuelva arriba)

**GET**

```http
{{baseUrl}}/api/data/v9.2/EntityDefinitions(LogicalName='<logical_name>')/Attributes?$select=LogicalName,SchemaName,AttributeType
```

Qué revisar:
- Confirmar exactamente los nombres de:
  - fecha de lectura
  - contador copias
  - contador escáner
  - lookup hacia equipo

## 9) Qué necesito que me compartas para corregirlo rápido

Pégame (puedes anonimizar IDs si quieres):

1. Response de paso 3 (equipos por cliente).
2. Response de paso 4 y 5.
3. Response de paso 6 (y 7 si aplica).
4. Response de paso 8.1 (EntitySetName real).
5. Si hay error, pega el JSON completo (`error.code`, `error.message`).

Con eso ajustamos de inmediato el query exacto que necesita el portal.
