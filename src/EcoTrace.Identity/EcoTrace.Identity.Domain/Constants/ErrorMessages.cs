namespace EcoTrace.Identity.Domain.Constants;

public static class ErrorMessages
{
    public static class Generic
    {
        public const string RequiredField = "El campo {0} es obligatorio.";
        public const string InvalidId = "El identificador proporcionado no es válido.";
    }

    public static class Tenant
    {
        public const string NameRequired = "El nombre del tenant es obligatorio.";
        public const string TaxIdRequired = "El NIT o identificación fiscal del tenant es obligatorio.";
        public const string TaxIdAlreadyExists = "Ya existe un tenant registrado con el mismo NIT o identificación fiscal.";
        public const string InvalidTenantType = "El tipo de tenant especificado no es válido.";
        public const string TenantNotFound = "El tenant solicitado no fue encontrado.";
        public const string TenantInactive = "El tenant se encuentra inactivo.";
    }

    public static class User
    {
        public const string FullNameRequired = "El nombre completo del usuario es obligatorio.";
        public const string EmailRequired = "El correo electrónico es obligatorio.";
        public const string InvalidEmail = "El formato del correo electrónico no es válido.";
        public const string TenantRequired = "El usuario debe estar asociado a un tenant válido.";
        public const string UserNotFound = "El usuario solicitado no fue encontrado.";
        public const string RoleNotFound = "El rol solicitado no fue encontrado.";
        public const string PasswordRequired = "La contraseña es obligatoria.";
    }

    public static class Role
    {
        public const string RoleNameRequired = "El nombre del rol es obligatorio.";
        public const string RoleAlreadyExists = "El rol especificado ya existe.";
        public const string RoleNotFound = "El rol solicitado no fue encontrado.";
    }
}