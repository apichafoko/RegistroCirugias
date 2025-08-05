using System;

namespace RegistroCx.Domain;

public enum UserState
{
    Unknown = 0,
    NeedPhone = 1,
    NeedEmail = 2,
    NeedOAuth = 3,
    PendingOAuth = 4,
    Ready = 5
}
