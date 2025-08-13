# 🚀 Guía de Deploy a Railway

## Requisitos previos
- [ ] Cuenta en [Railway.app](https://railway.app)
- [ ] Código subido a GitHub/GitLab
- [ ] Base de datos PostgreSQL ejecutando la migración: `ALTER TABLE appointments ALTER COLUMN anestesiologo DROP NOT NULL;`

## Paso 1: Preparar el repositorio

### 1.1 Commitear los cambios
```bash
git add .
git commit -m "Preparar para deploy en Railway

- Agregar Dockerfile y configuración Railway
- Configurar puerto dinámico para Railway
- Actualizar variables de entorno

🤖 Generated with Claude Code

Co-Authored-By: Claude <noreply@anthropic.com>"
git push origin main
```

## Paso 2: Crear proyecto en Railway

### 2.1 Ir a Railway
1. Ve a [railway.app](https://railway.app)
2. Click en **"Start a New Project"**
3. Selecciona **"Deploy from GitHub repo"**
4. Autoriza Railway para acceder a tu GitHub
5. Selecciona el repositorio `RegistroCx`

### 2.2 Configurar la base de datos
1. En el dashboard del proyecto, click **"+ New"**
2. Selecciona **"Database"** → **"PostgreSQL"** 
3. Railway creará una base de datos automáticamente
4. Ve a la pestaña **"Data"** de la base de datos
5. Ejecuta la migración:
   ```sql
   ALTER TABLE appointments ALTER COLUMN anestesiologo DROP NOT NULL;
   ```

## Paso 3: Configurar variables de entorno

### 3.1 En el servicio principal
Ve a tu servicio → **"Variables"** → Agrega estas variables:

```bash
# Bot de Telegram
TELEGRAM_BOT_TOKEN=tu_token_del_bot

# Base de datos (Railway lo proporciona automáticamente como DATABASE_URL)
CONNECTION_STRING=${{Postgres.DATABASE_URL}}

# OAuth Google
GOOGLE_CLIENT_ID=tu_client_id.googleusercontent.com
GOOGLE_CLIENT_SECRET=tu_client_secret
GOOGLE_REDIRECT_URI=https://${{RAILWAY_PUBLIC_DOMAIN}}/oauth/callback

# OpenAI
OPENAI_API_KEY=tu_openai_api_key

# Configuración
ASPNETCORE_ENVIRONMENT=Production
```

### 3.2 Variables automáticas de Railway
Railway proporciona automáticamente:
- `PORT` - Puerto donde debe ejecutarse la app
- `RAILWAY_PUBLIC_DOMAIN` - Dominio público de tu app
- `DATABASE_URL` - URL de conexión a PostgreSQL

## Paso 4: Deploy automático

### 4.1 Railway detectará automáticamente:
- ✅ El `Dockerfile` para construir la imagen
- ✅ El `railway.toml` para configuración
- ✅ Las variables de entorno configuradas

### 4.2 El deploy iniciará automáticamente
1. Ve a la pestaña **"Deployments"**
2. Observa el progreso del build
3. Espera que aparezca ✅ **"Success"**

## Paso 5: Verificar el deploy

### 5.1 Verificar la salud de la app
1. Ve a **"Settings"** → **"Networking"**
2. Copia la URL pública (algo como `https://registrocx-production.up.railway.app`)
3. Visita `https://tu-dominio.railway.app/health`
4. Deberías ver: `{"status":"OK","timestamp":"...","environment":"Production","version":"1.0.0"}`

### 5.2 Verificar el bot
1. Visita `https://tu-dominio.railway.app/health/bot`
2. Deberías ver: `{"status":"healthy","botName":"nombre_de_tu_bot"}`

### 5.3 Probar el bot en Telegram
1. Abre Telegram
2. Busca tu bot
3. Envía `/start`
4. Prueba el flujo completo de registro de cirugía

## Paso 6: Configurar el webhook de Telegram

### 6.1 Automático
Railway configurará automáticamente el webhook cuando la app esté ejecutándose.

### 6.2 Manual (si es necesario)
Ejecuta este comando con tu token y dominio:
```bash
curl -X POST "https://api.telegram.org/bot<TU_TOKEN>/setWebhook" \
-H "Content-Type: application/json" \
-d '{"url":"https://tu-dominio.railway.app/webhook"}'
```

## Paso 7: Monitoreo

### 7.1 Logs en tiempo real
1. Ve a tu servicio en Railway
2. Click en la pestaña **"Logs"**
3. Observa los logs en tiempo real

### 7.2 Métricas
1. Ve a **"Metrics"** para ver:
   - CPU usage
   - Memory usage
   - Network traffic

## Troubleshooting

### ❌ Build falla
- Verifica que el `Dockerfile` esté en la raíz del proyecto
- Revisa los logs de build en Railway

### ❌ App no inicia
- Verifica las variables de entorno
- Revisa los logs de aplicación
- Asegúrate que el puerto esté configurado correctamente

### ❌ Bot no responde
- Verifica el webhook: `https://api.telegram.org/bot<TOKEN>/getWebhookInfo`
- Revisa los logs para errores de conexión
- Verifica que `TELEGRAM_BOT_TOKEN` esté configurado

### ❌ Error de base de datos
- Verifica que `CONNECTION_STRING` apunte a la base de datos de Railway
- Ejecuta la migración: `ALTER TABLE appointments ALTER COLUMN anestesiologo DROP NOT NULL;`

## ✅ ¡Listo!

Tu bot ahora está ejecutándose 24/7 en Railway con:
- ✅ Auto-restart si falla
- ✅ Logs centralizados
- ✅ Métricas de rendimiento
- ✅ SSL automático
- ✅ Base de datos PostgreSQL administrada
- ✅ Deploy automático desde GitHub