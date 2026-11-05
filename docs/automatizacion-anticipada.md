# Generación anticipada del mes evaluado

## Cambios

- CampaignSchedulePoint admite offsets enteros de **-31 a 365**, siempre desde el primer día del mes siguiente al evaluado. Rechaza meses no normalizados, fechas sin mes siguiente, offsets fuera de rango y desbordamientos mediante DateOnly. Se conservan horas ambiguas/inexistentes y la zona America/Monterrey.
- AutomationCalendarValidation reutiliza los límites y mantiene apertura < cierre y exclusividad de postulaciones entre meses. No exige que opiniones y postulaciones tengan periodos distintos.
- CampaignAutomationCycle recorre desde FirstEvaluationMonth **hasta el mes actual inclusive** según TimeProvider y America/Monterrey. Si el límite está en el futuro espera. Ya no interpreta el mes actual como incompleto para generación.
- CampaignAutomationService admite el mes actual, lo incluye entre pendientes si falta y utiliza EnsureAutomaticMonthAsync de CampaignService. Este método reutiliza CreateMonthAsync/BuildCampaign y el repositorio; no duplica la lógica de creación. La creación manual sigue limitada a meses completos anteriores.
- El worker conserva mensajes controlados de conflictos: indica el mes que no pudo crear y los meses que lo bloquean. Una apertura manual antigua no se cierra, se oculta ni se ignora.
- La vista previa es de solo lectura. Presenta históricos autorizados pendientes, actual pendiente, existentes sin modificación y futuros planificados. No se cambian contratos ni endpoints.

## Fórmula del calendario solicitado

Base = primer día del mes posterior al evaluado, en calendario de Monterrey.
Apertura = base - 8 días, 00:00.
Cierre = base + 7 días, 00:00.

Restar 8 días a la base equivale a restar 7 al último día del mes evaluado. El cierre al inicio del día 8 deja completos los primeros siete días del siguiente mes. El extremo de cierre sigue siendo exclusivo según el dominio.

Tabla de resultados esperados, revisada por inspección de la fórmula (no ejecución de tests):

| Mes evaluado | Apertura local | Cierre local |
|---|---|---|
| Octubre 2026 | 24/10/2026 00:00 | 08/11/2026 00:00 |
| Noviembre 2026 | 23/11/2026 00:00 | 08/12/2026 00:00 |
| Febrero 2026 | 21/02/2026 00:00 | 08/03/2026 00:00 |
| Febrero 2028 | 22/02/2028 00:00 | 08/03/2028 00:00 |
| Diciembre 2026 | 24/12/2026 00:00 | 08/01/2027 00:00 |

Ambos periodos pueden usar estos mismos cuatro valores. Las opiniones continúan exigiendo candidatos preseleccionados: no se modificaron servicios ni políticas de opiniones, autenticación, roles o elegibilidad.

El rango permite otros calendarios. Valores negativos mayores en magnitud pueden situar una apertura antes del inicio del mes evaluado, especialmente en meses cortos. El generador no crea meses futuros: revisar siempre las fechas concretas de preview. La regla -8/+7 solicitada queda dentro del mes evaluado para todas sus longitudes.

## Datos, persistencia y concurrencia

No se escribió ninguna configuración, no se activó el generador y no se consultaron bases reales. Los valores actuales se conservan; el administrador debe guardar explícitamente -8/00:00 y 7/00:00 para cada periodo si desea ese calendario.

Los offsets son columnas SQL int sin checks que prohíban negativos. **No cambia el esquema: no se generó ni aplicó otra migración.** No se modificaron migraciones anteriores ni el archivo local preexistente migracion-produccion.sql.

Se conservan índice único EvaluationMonth, RowVersion, auditoría, CampaignAdministrationLock, transacciones, relectura de configuración bajo bloqueo y reintentos. La nueva comprobación explicativa de conflictos usa CampaignNominationPolicy dentro del mismo bloqueo. El repositorio mantiene su propia validación final antes del INSERT.

Cada mes se confirma de forma independiente. Un error puede dejar históricos anteriores ya creados; el ciclo se detiene y reintenta en la siguiente comprobación. Una convocatoria existente se devuelve sin cambiar periodos, modos, archivo ni datos relacionados. Pausar o guardar el calendario tampoco reprograma las existentes.

## Activación en noviembre de 2026

Si el permiso técnico y el administrativo están habilitados, no existe calendario heredado bloqueante y FirstEvaluationMonth permite octubre:

1. El siguiente ciclo revisa los meses autorizados, en orden cronológico.
2. Crea octubre si falta y no hay conflicto.
3. Crea noviembre si falta y no hay conflicto. No espera a diciembre.
4. Omite los existentes sin reprogramarlos. No crea diciembre todavía.

Con los nuevos valores explícitamente guardados, octubre abre 24/10 y cierra 08/11; noviembre abre 23/11 y cierra 08/12. Activar después de una apertura puede crear una convocatoria cuyo periodo ya esté abierto o cerrado; no recupera postulaciones u opiniones omitidas ni extiende plazos.

Una convocatoria antigua abierta manualmente puede bloquear ventanas no terminadas. Por ejemplo, febrero de 2026 abierto manualmente puede impedir crear noviembre: el error identifica febrero y requiere una decisión administrativa explícita sobre sus periodos. No hay cierres automáticos de excepciones.

La creación se permite desde el día 1; no se garantiza ejecución exacta a medianoche ni antes de una apertura si el sistema está pausado, sin disponibilidad o en conflicto. Los periodos Automatic se abren/cierran por evaluación de sus fechas, sin escrituras programadas.

## Archivos backend

- src/EmpleadoDelMes.Application/Campaigns/CampaignSchedule.cs
- src/EmpleadoDelMes.Application/Campaigns/CampaignService.cs
- src/EmpleadoDelMes.Application/CampaignAutomation/AutomationCalendarValidation.cs
- src/EmpleadoDelMes.Application/CampaignAutomation/CampaignAutomationService.cs
- src/EmpleadoDelMes.Api/Automation/CampaignAutomationCycle.cs
- tests/EmpleadoDelMes.UnitTests/Campaigns/CampaignAutomationTests.cs
- tests/EmpleadoDelMes.UnitTests/Campaigns/PersistentAutomationTests.cs
- docs/campaign-automation.md
- docs/automatizacion-anticipada.md

El código de pruebas se adaptó a la nueva semántica y se agregaron escenarios para las longitudes mensuales, cambio de año, límites, mes actual y conflictos manuales. **No se ejecutó ningún test**. Solo se compiló ese código.

## Verificación y pendientes

- dotnet build -c Release --no-restore: correcto, sin errores ni advertencias.
- Revisión estática de rango inclusivo, pendientes, zona horaria, protección de existentes, pausas y bloqueo técnico; los mecanismos de exclusividad SQL se conservan.
- git diff --check: sin errores.
- No se verificó concurrencia real ni integración contra SQL Server, Azure o navegador.
- Antes de un despliegue autorizado, revisar y guardar deliberadamente el calendario deseado. Al habilitar esta versión, el worker puede crear inmediatamente el mes actual usando los valores que ya estaban guardados. Si esos valores aún son antiguos, la convocatoria conservará ese calendario aunque luego cambie la configuración general. Coordinar la pausa y el guardado antes de permitir el primer ciclo.
- La validación real multiinstancia y la ejecución futura de la suite requieren una tarea autorizada; los resultados históricos de la documentación no validan este cambio.
