# Administrador inicial

El bootstrap se ejecuta desde `Program.cs`, antes de `app.Run()`. Está deshabilitado por defecto. No migra ni crea el esquema: requiere la base ya preparada cuando se habilita.

## Configuración segura

Claves: `AdminBootstrap:Enabled`, `AdminBootstrap:UserName`, `AdminBootstrap:Password`.

**Desarrollo:** el proyecto Api ya tiene `UserSecretsId`. En Visual Studio, usa **Administrar secretos de usuario** sobre ese proyecto y agrega las tres claves, con `Enabled` en `true`, un usuario administrativo elegido y una contraseña única. El archivo se mantiene fuera del repositorio. No pegues contraseñas en comandos que queden en el historial, logs o capturas. User Secrets es almacenamiento local de desarrollo y no cifra los secretos.

La contraseña debe pasar los validadores de Identity configurados actualmente: mínimo 12 caracteres y 4 caracteres distintos; se permiten frases de contraseña. El usuario debe respetar los caracteres admitidos por Identity, tener como máximo 256 caracteres y no incluir espacios exteriores. No se asigna empleado a esta cuenta.

**Azure App Service:** configura `AdminBootstrap__Enabled`, `AdminBootstrap__UserName` y `AdminBootstrap__Password` como ajustes de aplicación. Puedes usar referencias de App Service a secretos de Azure Key Vault para los valores sensibles. App Service resuelve esas referencias con su identidad administrada y los permisos de lectura correspondientes; la aplicación consume los valores mediante `IConfiguration`, sin código ni paquetes adicionales. Esto no configura un proveedor directo de Key Vault fuera de App Service. Usa ajustes específicos del slot y no habilites bootstrap en slots de pruebas contra la base de producción.

No hay valores predeterminados, credenciales versionadas ni endpoint de inicialización.

## Ejecución y protección frente a repeticiones

1. Ausencia de `Enabled` o `false`: no se resuelve Identity ni persistencia y no se realiza ninguna consulta de bootstrap.
2. Si se habilita, valida la configuración y la política de contraseña. Un error detiene el arranque con un mensaje controlado, sin valores sensibles ni excepciones internas. Mientras siga habilitado, la configuración debe seguir siendo válida incluso en reinicios.
3. Una transacción adquiere un bloqueo exclusivo `sp_getapplock`, propiedad de la transacción, compartido por todas las instancias conectadas a esa base. Espera hasta 15 segundos; si no se obtiene, aborta el arranque. No usa un bloqueo exclusivamente en memoria en producción.
4. Consulta la marca persistente `EmpleadoDelMes:AdminBootstrap = Completed:v1`, almacenada como claim del rol Administrator en `AspNetRoleClaims`. No representa un permiso ni se utiliza en políticas de autorización. Si existe, no realiza escrituras.
5. Si ya existe un Administrator y no hay marca, registra únicamente esa marca. No modifica usuarios, contraseñas ni crea roles faltantes en este caso.
6. En la primera creación, prepara los cinco roles faltantes, crea la cuenta con UserManager y la contraseña configurada, asigna Administrator y guarda la marca. Todo queda en la misma transacción; cualquier fallo revierte roles, cuenta, asignación y marca. Un nombre ocupado por otra cuenta produce un error: nunca se eleva ni se restablece esa cuenta.

La cuenta comienza con `MustChangePassword=true` y `LockoutEnabled=true`. Identity almacena el hash; el bootstrap no guarda la contraseña en texto plano ni la devuelve. Los logs propios solo comunican finalización u omisión, sin usuario ni secretos.

La marca sobrevive a la eliminación de usuarios y evita crear otro administrador aunque se cambie el nombre configurado. Debe conservarse el rol Administrator y su claim de inicialización. Borrar deliberadamente ambos, o restaurar una copia anterior al bootstrap, elimina esa evidencia: el bootstrap no es un mecanismo de recuperación de administradores. Una pérdida de acceso requiere un procedimiento administrativo autorizado.

## Después del primer uso

Desactiva `AdminBootstrap:Enabled` o elimina esa clave y **retira también UserName y Password de User Secrets/App Service/Key Vault según corresponda**. Realiza el primer cambio de contraseña por el flujo autenticado existente. No vuelvas a habilitar bootstrap para recuperar o agregar administradores; la marca impedirá nuevas creaciones.

## Validación y límites

Las pruebas utilizan UserManager/RoleManager reales y almacenes dobles con rollback y exclusión mutua simulados. Verifican reinicios, eliminación posterior, errores y concurrencia lógica. No demuestran el bloqueo físico o las transacciones de SQL Server: durante esta tarea no se ejecutó bootstrap contra una base real.

Referencias: [bloqueos de aplicación de SQL Server](https://learn.microsoft.com/en-us/sql/relational-databases/system-stored-procedures/sp-getapplock-transact-sql), [referencias de Key Vault en App Service](https://learn.microsoft.com/en-us/azure/app-service/app-service-key-vault-references), [User Secrets](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets).
