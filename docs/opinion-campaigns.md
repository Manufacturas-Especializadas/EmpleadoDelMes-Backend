# Consulta departamental de convocatorias

GET `/api/opinions/campaigns?pageNumber=1&pageSize=20`

Requiere Bearer válido y rol HumanResources, Safety o Quality. Se conservan los controles actuales de cuenta activa, bloqueo, security stamp y cambio obligatorio de contraseña. Las cuentas Administrator y Sponsor no tienen acceso, incluso si combinan esos roles con uno departamental. Esta consulta no elige un departamento para escribir opiniones: admite varios roles departamentales para consultar el calendario, sin cambiar la restricción existente en la captura de opiniones.

Respuesta 200:

```json
{
  "items": [
    {
      "id": "11111111-1111-1111-1111-111111111111",
      "evaluationMonth": "2026-10-01",
      "canAcceptOpinions": false,
      "isArchived": false
    }
  ],
  "total": 1,
  "pageNumber": 1,
  "pageSize": 20
}
```

Incluye convocatorias abiertas, cerradas y archivadas, ordenadas por EvaluationMonth descendente y por ID descendente como desempate. No filtra por candidatos existentes. Una lista vacía devuelve Items=[] y Total=0; una página fuera del rango conserva Total y devuelve Items=[].

PageNumber debe ser al menos 1 y PageSize entre 1 y 100. Se rechazan desplazamientos superiores a int.MaxValue. Valores predeterminados: página 1, tamaño 20. HTTP: 400 parámetros inválidos; 401 sesión ausente/inválida; 403 falta de permisos o contraseña temporal; 503 fallo de persistencia. Los mensajes de negocio están en español y no contienen errores internos del proveedor.

CampaignService.ListForOpinionsAsync reutiliza ListAsync para validación, errores y paginación, y reduce la respuesta a los cuatro campos públicos. CampaignRepository conserva su consulta AsNoTracking: un COUNT y una consulta paginada SQL, sin N+1 ni carga de todas las convocatorias. Se reutiliza la proyección administrativa interna de la página (hasta 100 convocatorias); sus campos adicionales no se exponen. CanAcceptOpinions se calcula mediante Campaign.CanAcceptOpinions y TimeProvider, conservando las reglas automáticas, manuales y de archivo. No se duplica esa lógica ni se modifica el contrato administrativo.

No se añaden escrituras, roles, migraciones ni tareas automáticas. Las lecturas de total y página pueden observar cambios concurrentes. Las pruebas HTTP usan Identity y repositorios dobles, no SQL Server real.

## Azure

No se requiere ningún cambio de configuración en Azure App Service, variables de entorno, secretos, roles ni esquema de base de datos. Para disponer de la nueva ruta bastará desplegar el backend actualizado mediante el procedimiento habitual; esta tarea no realiza despliegues. El frontend podrá consumir la ruta nueva sin cambiar sus rutas de navegación. GET /api/campaigns conserva su autorización administrativa.
