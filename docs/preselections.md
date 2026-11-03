# Preselección administrativa — 7A

Endpoints protegidos por Administrator:

- POST /api/admin/preselections/{campaignId}/{candidateEmployeeId}/select
- POST /api/admin/preselections/{campaignId}/{candidateEmployeeId}/withdraw

Enviar JSON `{}` para una primera selección, o `{"version":"BASE64"}` para cambiar un registro existente. La versión debe representar 8 bytes. El actor se obtiene de la sesión; los campos adicionales del cuerpo se rechazan. El cambio devuelve CampaignId, CandidateEmployeeId, IsSelected, LastChangedByUserId, LastChangedAtUtc y Version actualizada.

Application valida identificadores, versión, existencia de convocatoria y candidato, archivo y elegibilidad. Seleccionar requiere al menos una postulación no anulada en esa convocatoria, incluso si ya estaba seleccionado. No requiere un periodo abierto. Un registro retirado se reutiliza con Select; retirar utiliza Withdraw sin eliminarlo y no requiere postulaciones vigentes. Convocatorias archivadas no admiten ninguna operación.

Repetir el estado deseado devuelve 200 sin modificar actor, fecha ni RowVersion. Si se proporciona una versión obsoleta se responde 409, incluso para una repetición. Una transición existente exige versión; omitirla produce 400. Un retiro inexistente produce 404. Conflictos de inserción, concurrencia, archivo o elegibilidad producen 409; errores de persistencia, 503. Sesiones y permisos conservan los controles 401/403 existentes.

Infrastructure inicia una única transacción ReadCommitted y adquiere CampaignAdministrationLock antes de leer convocatoria, elegibilidad y preselección. Comparte recurso y orden con anulaciones y archivado; la comprobación y escritura no pueden intercalarse con esos controles. La clave compuesta evita duplicados y RowVersion se compara antes del cambio y se utiliza como valor original en EF Core. Los fallos revierten la transacción; solo se desasocia la preselección intentada. No se guardan cambios en postulaciones ni opiniones. TimeProvider aporta la fecha de cambios reales.

El listado 6C conserva sus campos y agrega IsPreselected y PreselectionVersion (null si no existe). Un LEFT JOIN por convocatoria y candidato obtiene el estado; agrupamiento, conteo, orden y paginación siguen en SQL. Solo la conversión Base64 de las versiones de la página se realiza en memoria.

## Limitaciones deliberadas

No hay retiro automático, historial completo ni auditoría adicional. Si se anula la última postulación válida, el candidato desaparece de 6C aunque su preselección persistida siga activa; tampoco se eliminan opiniones. Una anulación posterior a una selección ya confirmada puede producir ese estado y requiere una política futura.

El listado elegible no sirve para recuperar versiones de candidatos que perdieron todas sus postulaciones válidas. Para retirar estos registros se necesita una versión conservada de una respuesta anterior; una consulta administrativa de preselecciones históricas queda pendiente de autorización. No se permite omitir la versión para forzar cambios.

No se añadieron pruebas por decisión del proyecto. Queda pendiente comprobar con SQL Server real la traducción del LEFT JOIN, versiones generadas, duplicados concurrentes, rollback y coordinación entre instancias. La suite existente solo detecta regresiones. No se ejecutaron selecciones contra bases reales ni se generaron migraciones.
