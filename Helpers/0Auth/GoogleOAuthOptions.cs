using System;

namespace RegistroCx.Helpers._0Auth;

public class GoogleOAuthOptions
    {
        public string ClientId { get; set; } = "";
        public string ClientSecret { get; set; } = "";
        public string RedirectUri { get; set; } = "";
        public string AuthBase { get; set; } = "https://accounts.google.com/o/oauth2/v2/auth";
        public string TokenEndpoint { get; set; } = "https://oauth2.googleapis.com/token";
    }
