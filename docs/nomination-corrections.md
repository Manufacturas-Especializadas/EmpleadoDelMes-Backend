# Correcciones administrativas — 6B.3

El estado local incluía 6B.1 (listado con convocatoria, texto, estado de anulación y paginación). No incluía 6B.2; se agregó el detalle necesario para obtener la versión antes de corregir.

- `GET /api/admin/nominations/{id}`: responde con `nomination` (los campos del DTO de listado), `createdByUserId`, `justification` y `version` (RowVersion en Base64).
- `PUT /api/admin/nominations/{id}/correction`: exige Administrator, sesión válida y cambio de contraseña completado. Recibe exclusivamente `version`, `qualities`, `justification` y `reason`. Los campos adicionales, incluidos ActorUserId y las identidades de convocatoria/patrocinador/candidato, se rechazan.

La versión debe decodificarse a 8 bytes. El motivo, normalizado con Trim, debe tener entre 1 y 1000 caracteres. La validación de cualidades y justificación utiliza `Nomination.Correct(qualities, justification)` y conserva su normalización. No se cambian identidades, instantáneas, fecha ni autor de creación, anulación, preselecciones u opiniones.

Se permite corregir tras el cierre de postulaciones. Se rechazan con 409 las postulaciones anuladas y las convocatorias archivadas. No se exigen periodos abiertos ni se modifican calendarios.

## Atomicidad y concurrencia

El repositorio inicia una transacción y adquiere el mismo bloqueo de aplicación de administración de convocatorias antes de consultar el estado. Esto serializa la comprobación de archivo con Archive/Reopen sin agregar otro recurso de bloqueo. La versión se compara con la lectura actual antes de invocar la corrección y se utiliza como valor original de RowVersion en EF Core, que también protege el UPDATE frente a escrituras concurrentes externas.

Application registra una `NominationAuditEntry` con acción Correction, actor obtenido del principal autenticado, fecha UTC de TimeProvider y motivo. Details contiene JSON con Before y After de cualidades (valor numérico Flags) y justificación. No se registran secretos ni datos de autenticación.

La modificación y el INSERT de auditoría se envían con un único SaveChanges dentro de la misma transacción explícita. Solo se confirma después de completar ambas escrituras. Un fallo o conflicto revierte toda la operación; se desasocian las dos entidades intentadas para no conservar cambios rechazados en el contexto.

La respuesta 200 contiene `detail`, `changed` y `message`. En éxito, `detail.version` contiene la nueva versión. Si los valores después de normalizar son idénticos, devuelve `changed=false`, conserva la versión y no escribe ni genera auditoría. El motivo sigue siendo obligatorio. Una versión obsoleta se rechaza incluso si la solicitud no produciría cambios.

HTTP: 400 datos inválidos; 401 sin identidad válida; 403 sin permisos; 404 inexistente; 409 versión/conflicto/archivo/anulación; 503 persistencia. Los errores del proveedor no se devuelven al cliente.

No se añadieron pruebas automatizadas por decisión del proyecto. La compilación y la suite existente solo verifican compatibilidad y regresiones; queda pendiente validar este flujo nuevo contra SQL Server en un entorno autorizado: UPDATE con RowVersion, devolución de versión generada, carreras con archivado, fallo del INSERT de auditoría y rollback completo. No se ejecutaron correcciones reales ni migraciones.
