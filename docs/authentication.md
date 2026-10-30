# Autenticación HTTP (Tarea 4C)

Se usa el esquema `Identity.Bearer` y el handler oficial de ASP.NET Core. Los tokens son opacos, protegidos mediante Data Protection; no son JWT. No se publica `MapIdentityApi` ni registro público.

## Contrato

- `POST /api/auth/login`: `{ "userName": "...", "password": "..." }`. Devuelve el cuerpo nativo de Identity: `tokenType`, `accessToken`, `expiresIn` (segundos), `refreshToken`. La cabecera `X-Must-Change-Password` (`true`/`false`) informa el primer acceso y está expuesta en CORS. React también puede consultar `/me`.
- `GET /api/auth/me`: exige `Authorization: Bearer <accessToken>`. Devuelve `userId`, `userName`, `roles`, `displayName` (nullable) y `mustChangePassword`, obtenidos de los almacenes actuales.
- `POST /api/auth/change-password`: exige Bearer y `{ "currentPassword": "...", "newPassword": "..." }`. Reutiliza `IAccountService`. Al completar el cambio responde 200 y solicita iniciar sesión nuevamente; no emite nuevos tokens en esa respuesta.
- `POST /api/auth/refresh`: `{ "refreshToken": "..." }`, sin necesidad de token de acceso. Desprotege el ticket mediante `RefreshTokenProtector`, verifica vencimiento, existencia de cuenta, bloqueo y security stamp. Emite tokens mediante el handler oficial con los roles actuales. Una contraseña temporal impide renovar (403).

Solicitudes inválidas: 400. Credenciales incorrectas, cuentas bloqueadas o sesiones inválidas: 401. Permisos insuficientes o contraseña temporal: 403. Conflictos de concurrencia: 409. Fallos controlados de persistencia: 503. Los mensajes de login son uniformes para cuentas desconocidas, contraseñas erróneas y bloqueo.

## Protección central

El orden es routing, CORS, autenticación, validación de sesión actual y autorización. La política fallback exige autenticación; `/api/ping`, `/health` y la documentación de desarrollo conservan acceso anónimo. Login y refresh son anónimos respecto del token de acceso, pero validan sus propias credenciales.

Para cada solicitud autenticada a una ruta protegida se consulta la cuenta, se comprueba su security stamp y bloqueo y se reconstruye el principal con los roles actuales. No se cachea este estado. Un cambio de contraseña invalida tanto tokens de acceso como de renovación anteriores en la siguiente comprobación, sin esperar a su expiración. Esto no cancela operaciones que ya pasaron la comprobación y están en curso.

Si `MustChangePassword` está activo, el middleware permite únicamente las acciones marcadas explícitamente de `/me` y `/change-password`. También restringe rutas con `[Authorize]`, políticas de roles y política fallback. Las políticas reutilizables tienen los nombres de `AccountRoles`: Administrator, Sponsor, HumanResources, Safety y Quality. No se agregaron rutas de negocio.

## Ciclo de vida y despliegue

- Acceso: 15 minutos. Renovación: 8 horas desde cada emisión.
- La renovación nativa no es de un solo uso: el refresh anterior sigue siendo válido hasta su vencimiento, cambio de security stamp o bloqueo/eliminación de cuenta. No se implementa detección de reutilización ni duración absoluta de sesión.
- Al cambiar la contraseña, Identity actualiza el security stamp dentro del flujo transaccional existente. Todas las sesiones anteriores quedan invalidadas, no solo la que realizó el cambio.
- Cerrar sesión en el cliente significa eliminar **ambos** tokens. No existe endpoint logout ni revocación individual del lado servidor: una copia de un token eliminado del cliente continúa siendo utilizable bajo las reglas anteriores. No se presenta esa eliminación local como revocación.
- Para producción deben configurarse almacenamiento persistente y protección de las claves de Data Protection, y claves compartidas entre instancias. Perder o cambiar el anillo de claves invalida tokens; no se versionan claves ni secretos.
- Se requiere HTTPS y almacenamiento seguro en el cliente. No se habilita registro de cuerpos de peticiones ni de tokens. Las respuestas de autenticación no se almacenan en caché.
- Estas comprobaciones requieren disponibilidad del almacén Identity. Ante fallos se deniega la operación; no se reutilizan permisos de un token sin validación actual.

Las pruebas HTTP usan el host ASP.NET Core, handler Bearer, UserManager y RoleManager reales con almacenes dobles. No verifican persistencia física ni restricciones de SQL Server.

Referencia del flujo oficial de renovación: [IdentityApiEndpointRouteBuilderExtensions (.NET 10)](https://github.com/dotnet/aspnetcore/blob/v10.0.0/src/Identity/Core/src/IdentityApiEndpointRouteBuilderExtensions.cs). Se reutilizan sus capacidades sin mapear sus rutas adicionales.
