# 🔧 Troubleshooting Railway Deploy

## Error: ".NET SDK does not support targeting .NET 9.0"

### Problema
```
error NETSDK1045: The current .NET SDK does not support targeting .NET 9.0. 
Either target .NET 6.0 or lower, or use a version of the .NET SDK that supports .NET 9.0.
```

### Soluciones (en orden de preferencia)

## ✅ Solución 1: Usar .NET 9.0 con Docker (Recomendado)
El Dockerfile ya está configurado para .NET 9.0. Si sigue fallando:

1. **Verificar que Railway use Docker:**
   - Ve a tu proyecto en Railway
   - En "Settings" → "Build" → asegúrate que esté usando "Dockerfile"

2. **Si no detecta el Dockerfile automáticamente:**
   - Ve a "Settings" → "Build" 
   - Cambia "Builder" a "Docker"
   - Especifica "Dockerfile Path": `Dockerfile`

## ✅ Solución 2: Usar .NET 8.0 LTS (Más compatible)

Si .NET 9.0 sigue dando problemas, cambia a .NET 8.0:

### 2.1 Cambiar el TargetFramework
Edita `RegistroCx.csproj`:
```xml
<TargetFramework>net8.0</TargetFramework>
```

### 2.2 Usar Dockerfile alternativo
Renombra los archivos:
```bash
mv Dockerfile Dockerfile.net9
mv Dockerfile.net8 Dockerfile
```

### 2.3 Actualizar global.json
```json
{
  "sdk": {
    "version": "8.0.100",
    "rollForward": "latestMinor"
  }
}
```

## ✅ Solución 3: Usar Nixpacks (Railway nativo)

Si Docker no funciona, puedes forzar que Railway use su sistema nativo:

### 3.1 Eliminar archivos Docker
```bash
rm Dockerfile
rm railway.toml
```

### 3.2 Cambiar a .NET 8.0
Edita `RegistroCx.csproj`:
```xml
<TargetFramework>net8.0</TargetFramework>
```

### 3.3 Crear nixpacks.toml
```toml
[phases.build]
cmds = ['dotnet restore', 'dotnet build -c Release']

[phases.deploy]
cmd = 'dotnet RegistroCx.dll'

[variables]
DOTNET_VERSION = '8.0'
```

## 🔍 Verificación después del cambio

### Build local
```bash
dotnet clean
dotnet restore
dotnet build
```

### Commit y push
```bash
git add .
git commit -m "Cambiar a .NET 8.0 para compatibilidad Railway"
git push origin main
```

### Verificar en Railway
1. Ve a "Deployments" en Railway
2. Observa que el build complete exitosamente
3. Verifica en `/health` que la app esté funcionando

## 🎯 ¿Cuál elegir?

### Usa .NET 9.0 si:
- ✅ Quieres las últimas características
- ✅ Railway detecta y usa Docker correctamente
- ✅ El build de Docker funciona sin problemas

### Usa .NET 8.0 si:
- ✅ Quieres máxima compatibilidad (LTS)
- ✅ Railway tiene problemas con .NET 9.0
- ✅ Prefieres una versión más estable en producción

## 📞 Si nada funciona

1. **Revisar logs de Railway:**
   - Ve a "Logs" en tu proyecto
   - Busca errores específicos durante el build

2. **Probar build local:**
   ```bash
   docker build -t registrocx .
   docker run -p 8080:8080 registrocx
   ```

3. **Contactar soporte de Railway:**
   - Incluir logs específicos del error
   - Mencionar que necesitas .NET 9.0 support