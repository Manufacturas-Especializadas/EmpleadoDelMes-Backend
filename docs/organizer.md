# Organizador del proceso de reconocimiento

`Organizer` comparte con `Administrator` la política `RecognitionManagement`.
Permite administrar convocatorias, consultar/corregir/anular postulaciones,
seleccionar/retirar preselecciones, vincular evaluadores pendientes y consultar
la revisión integral con las cuatro opiniones. No altera las reglas de elegibilidad.

Usuarios, empleados, asignaciones generales, importaciones y automatización
continúan requiriendo `Administrator`. La validación de sesión activa y el
cambio obligatorio de contraseña siguen aplicándose antes de la autorización.

## Creación de cuentas

Solo Administrator puede ejecutar `POST /api/users/organizers`:

```json
{ "userName": "organizador" }
```

Reutiliza la respuesta de creación de cuentas existente (201), incluyendo la
contraseña temporal únicamente en esa respuesta. El nombre es obligatorio y
admite hasta 256 caracteres, sujeto también a los validadores de Identity.
Las solicitudes inválidas devuelven 400 y los nombres duplicados 409.
La nueva cuenta queda activa, sin EmployeeId, con MustChangePassword=true y
exclusivamente Organizer. Identity genera el hash de la contraseña aleatoria.
RoleManager crea el rol si falta dentro de la transacción de creación existente.
Un error impide conservar una cuenta parcialmente creada.

El directorio admite `GET /api/users?role=Organizer` y conserva su contrato.
El frontend ofrece la creación y el filtro en Usuarios y accesos, solo para
Administrator. La contraseña temporal se muestra en el diálogo ya existente.

## Navegación

Organizer inicia en `/organizer`. Reutiliza las pantallas bajo `/admin/campaigns`,
`/admin/nominations`, `/admin/preselections` y `/admin/review`. Sus guardas admiten
Administrator u Organizer. Las demás rutas administrativas mantienen la guarda
Administrator, incluso en navegación directa. El menú y el inicio del organizador
incluyen los cuatro módulos del proceso y las acciones generales de su cuenta.

## Despliegue y revisión manual pendiente

No se requieren migraciones, semillas ni variables nuevas en Azure. Publicar las
versiones compatibles del backend y frontend mediante el procedimiento habitual;
esta tarea no realiza despliegues ni crea cuentas reales. El rol se crea al solicitar
la primera cuenta desde el endpoint administrativo.

Verificar en un entorno autorizado la creación, rechazo de duplicados, primer
cambio de contraseña, acceso a los cuatro módulos y vinculación de evaluadores.
Comprobar 401 sin sesión y 403 de Organizer en usuarios, empleados, asignaciones,
importaciones y automatización, tanto por API como por navegación directa.
Revisar que Administrator conserva todos sus accesos y que los demás roles
conservan sus flujos. No se ejecutaron pruebas automatizadas ni consultas reales
a SQL Server para esta tarea. El modo demo del frontend no aprovisiona una cuenta
Organizer; la verificación del nuevo rol debe hacerse con el backend.
