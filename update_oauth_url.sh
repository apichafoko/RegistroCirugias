#!/bin/bash
# Script para actualizar la URL de OAuth callback

echo "🔧 Actualizando URL de OAuth callback"
echo "======================================"

# Pedir la URL de Railway al usuario
echo "Ve a https://railway.app y busca tu proyecto RegistroCx"
echo "Copia el dominio público (ejemplo: registrocx-production-abc123.up.railway.app)"
echo ""
read -p "Pega aquí tu dominio de Railway (sin https://): " railway_domain

if [ -z "$railway_domain" ]; then
    echo "❌ Error: Debes proporcionar el dominio de Railway"
    exit 1
fi

# Construir la URL completa
callback_url="https://$railway_domain/oauth/google/callback"

echo ""
echo "🔗 Nueva URL de callback: $callback_url"
echo ""

# Actualizar el archivo .env
if [ -f ".env" ]; then
    # Hacer backup
    cp .env .env.backup
    
    # Actualizar la línea GOOGLE_REDIRECT_URI
    sed -i.tmp "s|GOOGLE_REDIRECT_URI=.*|GOOGLE_REDIRECT_URI=$callback_url|" .env
    rm .env.tmp
    
    echo "✅ Archivo .env actualizado"
    echo "📋 Backup creado en .env.backup"
else
    echo "❌ Error: Archivo .env no encontrado"
    exit 1
fi

echo ""
echo "🏗️  SIGUIENTE PASO:"
echo "======================================"
echo "1. Ve a https://console.developers.google.com"
echo "2. Selecciona tu proyecto"
echo "3. Ve a Credenciales > OAuth 2.0 Client IDs"
echo "4. Edita tu Client ID"
echo "5. En 'Authorized redirect URIs' REEMPLAZA la URL de ngrok con:"
echo "   $callback_url"
echo "6. Guarda los cambios"
echo ""
echo "7. Redeploy tu app en Railway para que tome los nuevos valores"