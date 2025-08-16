#!/usr/bin/env python3
"""
Test del Sistema de Aprendizaje de Usuario
Simula interacciones para verificar que el aprendizaje funciona correctamente.
"""

import json
import requests
import time
from datetime import datetime

# Configuración del bot (cambiar por tu token real)
BOT_TOKEN = "TU_TOKEN_AQUI"  # Reemplazar con token real
CHAT_ID = 12345  # Reemplazar con tu chat ID de prueba

def send_telegram_message(text):
    """Envía un mensaje al bot de Telegram"""
    url = f"https://api.telegram.org/bot{BOT_TOKEN}/sendMessage"
    data = {
        "chat_id": CHAT_ID,
        "text": text
    }
    try:
        response = requests.post(url, json=data)
        return response.json()
    except Exception as e:
        print(f"Error enviando mensaje: {e}")
        return None

def test_learning_scenario():
    """
    Simula un escenario de aprendizaje completo
    """
    print("🧠 Iniciando Test del Sistema de Aprendizaje")
    print("=" * 50)
    
    # Escenario 1: Primera vez usando "cataratas"
    print("\n📝 Escenario 1: Primera interacción con 'cataratas'")
    message1 = "mañana cataratas quiroga callao 14hs"
    print(f"Enviando: {message1}")
    send_telegram_message(message1)
    print("✅ Mensaje enviado. El sistema debería aprender: 'cataratas' = 'FACOEMULSIFICACION'")
    time.sleep(3)
    
    # Escenario 2: Segunda vez usando "cataratas" - debería ser más rápido
    print("\n📝 Escenario 2: Segunda interacción con 'cataratas' (debería reconocer)")
    message2 = "pasado mañana cataratas quiroga 16hs"
    print(f"Enviando: {message2}")
    send_telegram_message(message2)
    print("✅ Mensaje enviado. El sistema debería usar el término aprendido más rápidamente")
    time.sleep(3)
    
    # Escenario 3: Usar abreviación "faco" 
    print("\n📝 Escenario 3: Primera vez usando 'faco'")
    message3 = "el viernes faco quiroga anchorena 10hs"
    print(f"Enviando: {message3}")
    send_telegram_message(message3)
    print("✅ Mensaje enviado. El sistema debería aprender: 'faco' = 'FACOEMULSIFICACION'")
    time.sleep(3)
    
    # Escenario 4: Usar término ya aprendido
    print("\n📝 Escenario 4: Usar término ya aprendido 'cataratas' nuevamente")
    message4 = "cataratas urgente"
    print(f"Enviando: {message4}")
    send_telegram_message(message4)
    print("✅ Mensaje enviado. Debería reconocer 'cataratas' inmediatamente")
    time.sleep(3)
    
    # Escenario 5: Aprender cirujano nuevo
    print("\n📝 Escenario 5: Nuevo cirujano 'magdi'")
    message5 = "cataratas magdi mañana 15hs"
    print(f"Enviando: {message5}")
    send_telegram_message(message5)
    print("✅ Mensaje enviado. Debería aprender: 'magdi' = nombre completo del cirujano")
    time.sleep(3)
    
    print("\n🎉 Test completado!")
    print("\n📊 Verificaciones esperadas:")
    print("1. Después del primer 'cataratas': sistema aprende el término")
    print("2. Después del segundo 'cataratas': respuesta más rápida")
    print("3. 'faco' también se asocia a FACOEMULSIFICACION")
    print("4. Términos de cirujanos se aprenden progresivamente")
    print("5. Contexto personalizado se incluye en validaciones futuras")

def test_database_learning():
    """
    Genera datos de prueba para simular aprendizaje en base de datos
    """
    print("\n🗄️ Generando SQL de prueba para el sistema de aprendizaje")
    
    # Datos de prueba para user_custom_terms
    test_data = """
-- Datos de prueba para el sistema de aprendizaje
-- Ejecutar después de create_user_learning_tables.sql

-- Términos de cirugías para usuario de prueba (chat_id: 12345)
INSERT INTO user_custom_terms (chat_id, user_term, standard_term, term_type, frequency, confidence, first_seen, last_used) VALUES
(12345, 'cataratas', 'FACOEMULSIFICACION', 'surgery', 5, 0.9, NOW() - INTERVAL '7 days', NOW()),
(12345, 'faco', 'FACOEMULSIFICACION', 'surgery', 3, 0.8, NOW() - INTERVAL '5 days', NOW()),
(12345, 'quiroga', 'Dr. Andrea Quiroga', 'surgeon', 8, 0.95, NOW() - INTERVAL '10 days', NOW()),
(12345, 'magdi', 'Dra. Magdi Rodriguez', 'surgeon', 3, 0.7, NOW() - INTERVAL '3 days', NOW()),
(12345, 'callo', 'Callao', 'place', 6, 0.85, NOW() - INTERVAL '8 days', NOW()),
(12345, 'ancho', 'Sanatorio Anchorena', 'place', 4, 0.8, NOW() - INTERVAL '6 days', NOW());

-- Patrones de comunicación para el usuario
INSERT INTO user_communication_patterns (chat_id, pattern_type, pattern_value, frequency, confidence, last_used) VALUES
(12345, 'frequent_surgery', 'FACOEMULSIFICACION', 8, 0.9, NOW()),
(12345, 'typical_surgeon', 'Dr. Andrea Quiroga', 8, 0.9, NOW()),
(12345, 'preferred_place', 'Callao', 6, 0.8, NOW()),
(12345, 'usual_quantity', '1', 10, 0.9, NOW());

-- Verificar que los datos se insertaron correctamente
SELECT 'Términos personalizados:' as info;
SELECT chat_id, user_term, standard_term, term_type, frequency, confidence 
FROM user_custom_terms 
WHERE chat_id = 12345 
ORDER BY frequency DESC;

SELECT 'Patrones de comunicación:' as info;
SELECT chat_id, pattern_type, pattern_value, frequency, confidence 
FROM user_communication_patterns 
WHERE chat_id = 12345 
ORDER BY frequency DESC;
"""
    
    with open('/Users/urielfraidenraij_1/Desktop/Projects/RegistroCx/test_learning_data.sql', 'w') as f:
        f.write(test_data)
    
    print("✅ Archivo test_learning_data.sql creado")
    print("💡 Para probar:")
    print("1. Ejecutar create_user_learning_tables.sql")
    print("2. Ejecutar test_learning_data.sql")
    print("3. Ejecutar las pruebas de Telegram")

def show_expected_context():
    """
    Muestra el contexto personalizado esperado después del aprendizaje
    """
    expected_context = """
=== CONTEXTO PERSONALIZADO USUARIO ===
TÉRMINOS DE CIRUGÍAS PERSONALIZADOS:
• "cataratas" = "FACOEMULSIFICACION" (usado 5 veces, confianza: 0.90)
• "faco" = "FACOEMULSIFICACION" (usado 3 veces, confianza: 0.80)

CIRUJANOS FRECUENTES:
• "quiroga" = "Dr. Andrea Quiroga" (usado 8 veces)
• "magdi" = "Dra. Magdi Rodriguez" (usado 3 veces)

LUGARES FRECUENTES:
• "callo" = "Callao" (usado 6 veces)
• "ancho" = "Sanatorio Anchorena" (usado 4 veces)

CIRUGÍAS MÁS FRECUENTES:
FACOEMULSIFICACION (8x), CERS (2x), HAVA (1x)

INSTRUCCIONES PERSONALIZADAS:
- Prioriza los términos aprendidos de este usuario sobre coincidencias genéricas
- Si el usuario usa un término conocido, aplica la traducción directamente
- Si hay ambigüedad, sugiere el término más usado por este usuario
=== FIN CONTEXTO PERSONALIZADO ===
"""
    
    print("\n📋 Contexto personalizado esperado:")
    print(expected_context)

if __name__ == "__main__":
    print("🚀 Sistema de Aprendizaje de Usuario - Test Suite")
    print("=" * 60)
    
    choice = input("""
Selecciona el tipo de test:
1. Test con Telegram (requiere bot token y chat ID)
2. Generar datos de prueba SQL
3. Mostrar contexto esperado
4. Todo lo anterior

Opción (1-4): """)
    
    if choice in ['1', '4']:
        if BOT_TOKEN == "TU_TOKEN_AQUI":
            print("⚠️  Configura BOT_TOKEN y CHAT_ID antes de ejecutar tests de Telegram")
        else:
            test_learning_scenario()
    
    if choice in ['2', '4']:
        test_database_learning()
    
    if choice in ['3', '4']:
        show_expected_context()
    
    print("\n✅ Test suite completado!")