# Candidatos elegibles por convocatoria — 6C

`GET /api/admin/nominations/candidates?campaignId={guid}&pageNumber=1&pageSize=20&term=...` exige la política Administrator y los controles de sesión existentes. La ruta literal candidates tiene prioridad sobre la ruta de detalle {id}.

CampaignId es obligatorio y no puede ser Guid.Empty. PageNumber debe ser al menos 1, PageSize entre 1 y 100 y el desplazamiento no puede superar int.MaxValue. Term es opcional, admite hasta 100 caracteres y se normalizan sus espacios externos.

El repositorio comprueba la existencia de la convocatoria. Después filtra Nominations por CampaignId e IsCancelled=false, agrupa por CandidateEmployeeId y obtiene COUNT. Une esos grupos con Employees para mostrar nombre y nómina actuales, conservando los ceros iniciales. No utiliza instantáneas históricas para la búsqueda. Un candidato aparece una sola vez y es elegible si tiene al menos una postulación válida en esa convocatoria, independientemente de sus postulaciones en otras convocatorias.

La búsqueda parcial por nombre o nómina se aplica antes de contar y paginar. Total representa candidatos filtrados, no postulaciones. El orden es conteo descendente, nómina ascendente e identificador ascendente. GroupBy, Count, filtros, Skip y Take se ejecutan mediante EF Core en SQL Server, sin materializar todas las postulaciones ni consultar individualmente por candidato. Se realizan tres consultas: existencia, total y página; se utiliza AsNoTracking.

La respuesta contiene Items, Total, PageNumber y PageSize. Cada elemento incluye CampaignId, CandidateEmployeeId, CandidatePayrollNumber, CandidateFullName y ValidNominationsCount. Una convocatoria inexistente responde 404; una existente sin candidatos elegibles devuelve 200 con Items vacío y Total=0. Una página fuera de los resultados devuelve Items vacío y conserva Total. Parámetros inválidos: 400; sesión ausente o inválida: 401; permisos insuficientes: 403; error de persistencia: 503, con mensaje en español sin detalles del proveedor.

La consulta no modifica datos ni exige que la convocatoria esté abierta. No crea ni retira preselecciones. Si un candidato preseleccionado pierde su última postulación válida, desaparece de esta lista, pero su preselección y opiniones permanecen intactas. La siguiente fase deberá aprobar una política de consistencia y revalidar la elegibilidad al seleccionar, pues esta consulta no reserva el estado del candidato.

No se incorporan pruebas nuevas por decisión del proyecto. Queda pendiente validar contra SQL Server real la traducción y el plan de ejecución, rendimiento y búsqueda según la collation configurada. Total y página son lecturas separadas: una inserción o anulación concurrente puede cambiar los resultados entre ambas o entre páginas; no se promete una instantánea transaccional. No se consultaron ni modificaron bases reales.
