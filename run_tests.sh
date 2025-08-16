#!/bin/bash
# Script para ejecutar testing automatizado de RegistroCx

echo "🤖 RegistroCx - Testing Automatizado"
echo "=================================="

# Verificar Python
if ! command -v python3 &> /dev/null; then
    echo "❌ Error: Python3 no encontrado"
    exit 1
fi

# Instalar dependencias si no existen
echo "📦 Instalando dependencias..."
pip3 install requests --quiet

# Verificar variables de entorno
if [ -z "$TELEGRAM_BOT_TOKEN" ] || [ -z "$TEST_CHAT_ID" ]; then
    echo "❌ Error: Variables de entorno faltantes"
    echo "Por favor configura:"
    echo "export TELEGRAM_BOT_TOKEN='tu_token_aquí'"
    echo "export TEST_CHAT_ID='tu_chat_id_aquí'"
    echo ""
    echo "📋 Ver archivo .env.testing para instrucciones"
    exit 1
fi

# Opciones de testing
echo "🚀 Selecciona tipo de testing:"
echo "1) Manual - Envía mensajes y documenta manualmente"
echo "2) Automatizado - Intenta detectar respuestas (puede fallar)"
echo ""
read -p "Selecciona (1 o 2): " choice

case $choice in
    1)
        echo "🧪 Ejecutando testing manual..."
        python3 test_manual_simple.py
        ;;
    2)
        echo "🤖 Ejecutando testing automatizado..."
        echo "⚠️  NOTA: Puede dar timeouts si no detecta respuestas del bot"
        python3 test_bot_automated.py
        ;;
    *)
        echo "❌ Opción inválida. Usando testing manual por defecto."
        python3 test_manual_simple.py
        ;;
esac

echo ""
echo "✅ Testing completado!"
echo "📊 Revisa los resultados en los archivos generados"