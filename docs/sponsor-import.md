# Importación de patrocinadores autorizados

Solo administradores autenticados con contraseña definitiva pueden utilizar:

- `POST /api/users/import/sponsors/preview`
- `POST /api/users/import/sponsors/confirm`

Ambas rutas reciben `multipart/form-data`, campo `file`, archivo `.xlsx` de hasta 5 MB, con encabezados `Nómina` y `Nombre completo` en la primera fila y hasta 1,000 registros. La confirmación exige además el campo `confirm=true`. El límite del cuerpo completo es 6 MB para admitir el envoltorio multipart.

Se reutiliza `ClosedXmlEmployeeReader`, incluidos sus encabezados alternativos y lectura de la primera hoja. Las nóminas son texto: `007` se conserva si la celda contiene ese texto o el valor 7 con formato `000`. Un valor 7 sin ese formato no permite reconstruir ceros que no existen en el archivo. Se rechazan fórmulas, incluso en columnas adicionales, y se marcan todas las filas de una nómina duplicada.

La vista previa devuelve `totalRows`, `newAccounts`, `skippedAccounts`, `errorRows` y `rows`. Cada fila incluye `rowNumber`, `payrollNumber`, `excelName`, `catalogName`, `status` y `message`. Los estados se serializan con estos valores:

| status | Significado |
|---|---|
| 0 | Cuenta lista para crearse |
| 1 | Sponsor existente, se omite |
| 2 | Sponsor desactivado, se omite sin activar |
| 3 | Empleado inexistente |
| 4 | Nómina duplicada en Excel |
| 5 | Nombre distinto del catálogo |
| 6 | Conflicto de cuenta, rol, vínculo o usuario |
| 7 | Datos inválidos |

Los nombres se comparan ignorando acentos, mayúsculas y espacios repetidos. Nunca se corrigen nombres del catálogo. Un Sponsor se omite únicamente si conserva el usuario correspondiente a su nómina, el vínculo correcto y exclusivamente el rol Sponsor. Los estados 3–7 bloquean todo el lote.

La confirmación vuelve a leer el archivo y consulta el estado actual dentro de una única transacción del `AccountTransactionRunner`. La creación interna de `IdentityAccountService` se reutiliza sin abrir transacciones por cuenta. Ante un error no se confirma ninguna cuenta del lote. Los índices únicos existentes de usuario normalizado y EmployeeId protegen ante altas concurrentes. Una colisión concurrente puede devolver 409; vuelve a consultar la vista previa y confirma nuevamente. Una repetición posterior exitosa omite las cuentas que ya existen.

Solo se crean cuentas de empleados presentes en el archivo y existentes en el catálogo: `UserName` y contraseña inicial corresponden a la nómina, `MustChangePassword=true`, `IsActive=true`, rol Sponsor. Se conserva la excepción de contraseña inicial Sponsor del código local mediante el hasher de Identity. No se relaja la política de contraseñas definitivas ni administrativas. Ninguna respuesta de esta importación incluye contraseñas, hashes o tokens. No se modifican roles, nombres, contraseñas ni estados de cuentas existentes.

La respuesta de confirmación contiene únicamente `totalRows`, `createdAccounts` y `skippedAccounts`. HTTP: 200 para vista previa (incluyendo filas inválidas) y confirmación exitosa; 400 para archivo/confirmación inválida o conflictos detectados al validar; 401 sin autenticación; 403 sin permisos o con contraseña temporal; 409 para conflicto concurrente; 503 para errores controlados de persistencia. Kestrel puede rechazar cuerpos superiores al límite total con 413.

No se modificaron los endpoints de importación de empleados. El lector compartido amplía el rechazo de fórmulas a las columnas adicionales. No hay cambios de esquema ni paquetes nuevos. Las pruebas usan Excel real en memoria, Identity real con almacenes dobles y el host HTTP de pruebas; no validan restricciones físicas de SQL Server.
