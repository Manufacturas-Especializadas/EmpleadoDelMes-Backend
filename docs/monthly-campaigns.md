# Convocatorias mensuales — Tarea 5A

`CampaignService.EnsurePreviousMonthAsync(DateOnly localReferenceDate)` garantiza la convocatoria del mes completo anterior. Normaliza al día 1 y obtiene `CreatedAtUtc` del `TimeProvider` registrado. No deduce la fecha local a partir del reloj UTC: el invocador debe proporcionar la fecha de la planta.

Ejemplos: 2026-11-03 corresponde a 2026-10-01; 2027-01-05 corresponde a 2026-12-01. Una nueva convocatoria conserva los valores iniciales del dominio: ambos periodos `ManuallyClosed`, sin horarios y sin archivar. Una existente se devuelve sin escrituras, conservando identificador, creación, archivo y periodos.

La consulta previa evita escrituras innecesarias, pero la protección concurrente definitiva es el índice único existente de `EvaluationMonth`. Si el INSERT falla por unicidad (SQL Server 2601/2627), el repositorio desasocia únicamente la entidad que intentó insertar y Application consulta de nuevo ese mes para devolver la ganadora. Si no aparece, devuelve un conflicto controlado. No se modificó el índice ni el esquema.

Otros casos de uso: `GetByIdAsync`, `GetByMonthAsync` (normaliza cualquier día al primero del mes) y `ListAsync`. Las consultas de persistencia usan `AsNoTracking`; el listado cuenta y pagina en SQL, ordenado por mes descendente y después por ID descendente. El total y la página son consultas independientes y pueden reflejar una inserción concurrente entre ambas.

## HTTP administrativo

- `GET /api/campaigns?pageNumber=1&pageSize=20`: tamaño entre 1 y 100; devuelve `items`, `total`, `pageNumber`, `pageSize`.
- `GET /api/campaigns/{id}`: devuelve la convocatoria; 404 si no existe y 400 si el identificador no es válido.

Los DTO incluyen `id`, `evaluationMonth`, `createdAtUtc`, `isArchived`, `nominationPeriod` y `opinionPeriod`. Cada periodo contiene `mode`, `scheduledOpeningAtUtc` y `scheduledClosingAtUtc`. Los modos actuales son 0 (automático), 1 (apertura manual) y 2 (cierre manual); los horarios sin configurar se devuelven como null.

Ambas rutas requieren la política Administrator y quedan sujetas al control central existente de contraseña temporal. No hay endpoint HTTP de creación ni consulta pública. Los errores de validación son 400, ausencia 404 y persistencia 503, con mensajes en español sin detalles internos.

## Integración posterior

Falta configurar el huso horario de la planta y un mecanismo de ejecución periódica que calcule la fecha local y llame al caso de uso dentro de un scope. Deberá definir reintentos y seguimiento operativo. Esta entrega no incorpora ejecución automática al arrancar, procesos en segundo plano ni recuperación de meses históricos faltantes.

Las pruebas utilizan repositorios dobles, incluido un conflicto de unicidad coordinado entre dos llamadas. No constituyen una prueba física del índice de SQL Server. No se creó ninguna convocatoria real ni migración.
