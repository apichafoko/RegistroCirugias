#!/usr/bin/env python3
"""
Script de testing manual simple para RegistroCx Bot
Envía casos de prueba y documenta manualmente los resultados
"""

import requests
import json
import time
from datetime import datetime
import os

# Casos de prueba simplificados
TEST_CASES = [
    {
        "id": "TC-001",
        "name": "Caso básico completo",
        "input": "2 CERS mañana 14hs Hospital Italiano Dr. García",
        "expected": "confirmás estos datos"
    },
    {
        "id": "TC-002", 
        "name": "Apendicectomía con fecha absoluta",
        "input": "Apendicectomía 15/08/2025 16:30 Sanatorio Anchorena Dr. López",
        "expected": "confirmás estos datos"
    },
    {
        "id": "TC-003",
        "name": "Información incompleta - solo cantidad",
        "input": "2 cirugías mañana",
        "expected": "tipo de cirugía"
    },
    {
        "id": "TC-004",
        "name": "Errores tipográficos múltiples",
        "input": "2 SERC mañana 14sh Hospital Itlaiano Dr. Garsia",
        "expected": "CERS"
    },
    {
        "id": "TC-005",
        "name": "Reporte semanal",
        "input": "/semanal",
        "expected": "Generando reporte semanal"
    }
]

def send_message(bot_token: str, chat_id: str, text: str) -> dict:
    """Envía mensaje al bot"""
    url = f"https://api.telegram.org/bot{bot_token}/sendMessage"
    data = {
        "chat_id": chat_id,
        "text": text
    }
    
    try:
        response = requests.post(url, data=data)
        return response.json()
    except Exception as e:
        return {"error": str(e)}

def main():
    print("🤖 RegistroCx Bot Manual Testing")
    print("=" * 50)
    
    # Configuración
    bot_token = os.getenv('TELEGRAM_BOT_TOKEN')
    chat_id = os.getenv('TEST_CHAT_ID')
    
    if not bot_token or not chat_id:
        print("❌ Error: Se requieren variables de entorno:")
        print("   TELEGRAM_BOT_TOKEN - Token del bot")
        print("   TEST_CHAT_ID - ID del chat de prueba")
        return
    
    print(f"📱 Chat ID: {chat_id}")
    print(f"🤖 Bot Token: {bot_token[:10]}...")
    print("-" * 50)
    
    results_file = f"manual_test_results_{datetime.now().strftime('%Y%m%d_%H%M%S')}.txt"
    
    with open(results_file, 'w', encoding='utf-8') as f:
        f.write("🧪 RESULTADOS DE TESTING MANUAL - RegistroCx\n")
        f.write("=" * 60 + "\n")
        f.write(f"Fecha: {datetime.now().isoformat()}\n")
        f.write(f"Chat ID: {chat_id}\n\n")
        
        for i, test_case in enumerate(TEST_CASES, 1):
            print(f"\n🧪 Test {i}/{len(TEST_CASES)}: {test_case['name']}")
            print(f"📤 Enviando: '{test_case['input']}'")
            
            # Enviar mensaje
            result = send_message(bot_token, chat_id, test_case['input'])
            
            if "error" in result:
                print(f"❌ Error enviando mensaje: {result['error']}")
                f.write(f"TEST {test_case['id']}: {test_case['name']}\n")
                f.write(f"Input: {test_case['input']}\n")
                f.write(f"Error: {result['error']}\n")
                f.write("-" * 40 + "\n\n")
                continue
            
            print(f"✅ Mensaje enviado exitosamente")
            print(f"💭 Esperado: {test_case['expected']}")
            print(f"⏱️  Revisa manualmente la respuesta del bot en Telegram...")
            
            # Escribir al archivo de resultados para documentación manual
            f.write(f"TEST {test_case['id']}: {test_case['name']}\n")
            f.write(f"Input: {test_case['input']}\n")
            f.write(f"Expected: {test_case['expected']}\n")
            f.write(f"Actual Response: [REVISAR MANUALMENTE EN TELEGRAM]\n")
            f.write(f"Status: [COMPLETAR MANUALMENTE: ✅PASS ❌FAIL ⚠️WARNING]\n")
            f.write(f"Notes: [AGREGAR OBSERVACIONES]\n")
            f.write("-" * 40 + "\n\n")
            
            # Pausa entre tests
            if i < len(TEST_CASES):
                time.sleep(3)
                print("⏳ Esperando 3 segundos antes del próximo test...")
    
    print(f"\n📊 Tests enviados completamente!")
    print(f"📝 Archivo de resultados creado: {results_file}")
    print(f"👀 IMPORTANTE: Debes completar manualmente los resultados revisando las respuestas en Telegram")
    print(f"📱 Ve a tu chat con el bot y documenta cada respuesta en el archivo {results_file}")

if __name__ == "__main__":
    main()