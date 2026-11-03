# Generación mensual automática — Tarea 5C

La automatización está **deshabilitada por defecto**. Esta entrega no ejecuta cambios contra bases reales ni habilita el proceso en ningún ambiente.

## Configuración

En `appsettings.json` se versionan únicamente valores no sensibles:

```json
{
  "CampaignAutomation": {
    "Enabled": false,
    "TimeZoneId": "America/Monterrey",
    "CheckIntervalMinutes": 60,
    "FirstEvaluationMonth": null
  }
}
```

Antes de activar, el responsable debe configurar expresamente el primer mes evaluado autorizado en formato `yyyy-MM-dd`, siempre día 1. Por ejemplo, `2026-10-01` autoriza octubre de 2026 como primer mes; no es un valor predeterminado del código. Un primer mes futuro es válido: el proceso espera sin consultar persistencia hasta que ese mes esté completo.

`Enabled` ausente o false no resuelve servicios de persistencia ni ejecuta consultas. Cuando es true se valida toda la configuración al arrancar: zona IANA reconocida, mes inicial válido e intervalo entero entre 1 y 1440 minutos (predeterminado 60). El límite de un día evita una configuración que compruebe con una frecuencia excesivamente baja. Los errores de configuración detienen el arranque con un mensaje en español, antes del trabajo SQL de automatización. No se sustituyen valores inválidos por la zona del sistema.

En desarrollo, configura estos valores mediante User Secrets del proyecto Api o variables de entorno. No actives el proceso sobre una base compartida sin autorizar primero el mes inicial y la base de destino. Cambiar configuración requiere reiniciar la API; el servicio toma una configuración validada al arrancar y no aplica recargas parciales. Para desactivar, establece `Enabled=false` (o elimina cualquier override true) y reinicia. No se eliminan las convocatorias existentes.

## Zona horaria y plataformas

El único origen del instante actual es `TimeProvider.GetUtcNow()`. Se convierte con `TimeZoneInfo.ConvertTime` a `America/Monterrey`; nunca se usa un offset fijo ni la fecha local del servidor. Se evalúa el mes completo anterior a la fecha local resultante. A las 00:30 UTC del 1 de noviembre de 2026 todavía es octubre en Monterrey: la convocatoria más reciente autorizada en ese instante es septiembre, no octubre.

.NET admite identificadores IANA tanto en Windows como en Linux mediante sus datos de globalización. El entorno debe disponer de ICU y datos horarios vigentes; no se recomienda globalization-invariant ni forzar NLS para esta función. En Linux, mantén ICU/tzdata de la imagen oficial actualizados. Una zona no disponible produce un error de configuración, sin fallback. [Referencia de TimeZoneInfo](https://learn.microsoft.com/en-us/dotnet/api/system.timezoneinfo.findsystemtimezonebyid?view=net-10.0).

Se comprobó `America/Monterrey` con las pruebas en Windows. No había un entorno Linux ejecutable en la estación (WSL no instalado y Docker no disponible), por lo que la ejecución Linux queda pendiente. Antes de activar en App Service Linux, ejecutar las pruebas en un entorno .NET 10 Linux con automatización deshabilitada; la prueba `IanaZone_ResolvesThroughPlatformData_AndDefaultIntervalIs60` comprueba la resolución y conversión real de plataforma sin SQL Server.

## Ejecución y recuperación

`CampaignAutomationWorker`, alojado en Api, es un BackgroundService específico con PeriodicTimer y TimeProvider. Comprueba al iniciar y después según el intervalo. Cada ejecución espera a que termine la anterior, sin callbacks concurrentes ni Task.Run propios. En .NET 10, ExecuteAsync se ejecuta fuera del hilo de arranque; un fallo o espera de SQL no bloquea la disponibilidad inicial de la API. La validación de configuración sí ocurre durante StartAsync. [Comportamiento de BackgroundService en .NET 10](https://learn.microsoft.com/en-us/dotnet/core/compatibility/extensions/10.0/backgroundservice-executeasync-task).

Cada comprobación crea un scope y llama a `CampaignService.EnsurePreviousMonthAsync` para cada mes entre FirstEvaluationMonth y el último mes local completo, pasando como referencia el inicio del mes siguiente. Así reutiliza todas las reglas existentes de 5A. Los meses ya existentes no reciben escrituras; los nuevos quedan sin archivar, con ambos periodos ManuallyClosed y sin horarios. **Crear una convocatoria no abre capturas.** Los controles de 5B siguen siendo responsabilidad del administrador.

Solo después de completar toda la comprobación se recuerda el mes local en memoria. Las siguientes comprobaciones de ese mes no consultan SQL. Cuando cambia el mes local, o arranca una nueva instancia, se revisa nuevamente todo el rango autorizado. La memoria es una optimización, no una garantía de unicidad ni un registro durable.

Si falla un mes, los anteriores ya confirmados permanecen. Se registra un evento controlado y en el siguiente intervalo se vuelve a recorrer el rango, recuperando lo que falta sin duplicar. Las excepciones y mensajes internos del proveedor no se incorporan a los logs propios. No se registran cadenas de conexión. La cancelación del host se transmite hasta EF; no se trata como error reintentable y libera el scope y el temporizador.

Entre instancias se reutiliza el índice único EvaluationMonth y la recuperación de 5A tras los errores de unicidad. No se modifica `sp_getapplock` de administración ni se abre ningún periodo. Las pruebas de dos instancias y fallos de persistencia usan dobles: falta validar la carrera física contra SQL Server en un entorno autorizado.

## Azure App Service (configuración futura)

Los ajustes de aplicación admiten los nombres:

- `CampaignAutomation__Enabled`
- `CampaignAutomation__TimeZoneId`
- `CampaignAutomation__CheckIntervalMinutes`
- `CampaignAutomation__FirstEvaluationMonth`

Mantener Enabled=false hasta autorizar la activación y verificar el destino de SQL. Configurar estos valores por slot para evitar habilitarlos accidentalmente en staging contra producción. No se necesitan nuevos recursos Azure, secretos de automatización ni endpoints de ejecución.

Habilitar **Always On** en un plan compatible al activar la función. Sin Always On, App Service puede descargar una aplicación después de 20 minutos sin solicitudes. Always On reduce la suspensión por inactividad, pero no evita reinicios, despliegues ni interrupciones. [Configuración de App Service](https://learn.microsoft.com/en-us/azure/app-service/configure-common#configure-general-settings).

Esto no es un programador de precisión: con intervalo de 60 minutos, la detección normal ocurre en la comprobación posterior al cambio de mes. El retraso puede ser mayor durante recuperación, indisponibilidad o suspensión. No garantiza ejecución a medianoche. Al volver a ejecutarse recupera los meses omitidos desde el límite configurado.

## Verificación operativa tras una activación autorizada

1. Verificar zona IANA, mes inicial y base de destino; activar y reiniciar únicamente con autorización.
2. Revisar el evento de comprobación completada (cantidad de meses verificados, no cantidad creada). Si hay advertencias, corregir la disponibilidad y esperar la siguiente comprobación.
3. Como Administrator con contraseña definitiva, consultar `GET /api/campaigns` recorriendo las páginas. Debe haber un registro por mes autorizado completo, con EvaluationMonth en día 1 y ninguno del mes local actual creado por el proceso.
4. Consultar `GET /api/campaigns/{id}`: las nuevas deben tener `isArchived=false`, modos 2 (ManuallyClosed), horarios null y captura efectiva deshabilitada. Las existentes deben conservar sus periodos, fechas y archivo.
5. Repetir tras un reinicio: no debe haber duplicados ni modificaciones a convocatorias existentes. Las consultas GET solo leen; no ejecutan la generación.
