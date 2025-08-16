#!/usr/bin/env python3
"""
Script automatizado para testing de RegistroCx Bot
Envía múltiples casos de prueba y documenta resultados
"""

import requests
import json
import time
import csv
from datetime import datetime
from typing import List, Dict, Any
import os

class BotTester:
    def __init__(self, bot_token: str, chat_id: str):
        self.bot_token = bot_token
        self.chat_id = chat_id
        self.base_url = f"https://api.telegram.org/bot{bot_token}"
        self.results = []
        
    def send_message(self, text: str) -> Dict[Any, Any]:
        """Envía mensaje al bot y retorna respuesta"""
        url = f"{self.base_url}/sendMessage"
        data = {
            "chat_id": self.chat_id,
            "text": text
        }
        
        try:
            response = requests.post(url, data=data)
            return response.json()
        except Exception as e:
            return {"error": str(e)}
    
    def get_updates(self) -> Dict[Any, Any]:
        """Obtiene últimas actualizaciones del bot"""
        url = f"{self.base_url}/getUpdates"
        try:
            response = requests.get(url)
            return response.json()
        except Exception as e:
            return {"error": str(e)}
    
    def wait_for_response(self, timeout: int = 30) -> str:
        """Espera respuesta del bot por un tiempo determinado"""
        start_time = time.time()
        last_update_id = 0
        
        # Obtener último update_id conocido
        updates = self.get_updates()
        if updates.get("result"):
            last_update_id = max([u["update_id"] for u in updates["result"]], default=0)
        
        while time.time() - start_time < timeout:
            updates = self.get_updates()
            if updates.get("result"):
                for update in updates["result"]:
                    if update["update_id"] > last_update_id:
                        # Buscar mensajes del bot en el chat correcto
                        if "message" in update:
                            msg = update["message"]
                            # Verificar que es del chat correcto Y que viene del bot (no del usuario)
                            if (msg.get("chat", {}).get("id") == int(self.chat_id) and 
                                msg.get("from", {}).get("is_bot") == True):
                                return msg.get("text", "")
            time.sleep(1)
        
        return "TIMEOUT - No response received"
    
    def run_test_case(self, test_case: Dict[str, Any]) -> Dict[str, Any]:
        """Ejecuta un caso de prueba específico"""
        print(f"🧪 Running test: {test_case['name']}")
        
        # Enviar mensaje
        send_result = self.send_message(test_case['input'])
        
        if "error" in send_result:
            return {
                "test_id": test_case['id'],
                "name": test_case['name'],
                "input": test_case['input'],
                "expected": test_case['expected'],
                "actual": f"ERROR: {send_result['error']}",
                "status": "FAIL",
                "timestamp": datetime.now().isoformat()
            }
        
        # Esperar respuesta
        time.sleep(2)  # Dar tiempo al bot para procesar
        response = self.wait_for_response()
        
        # Evaluar resultado
        status = self.evaluate_result(test_case['expected'], response)
        
        result = {
            "test_id": test_case['id'],
            "name": test_case['name'],
            "input": test_case['input'],
            "expected": test_case['expected'],
            "actual": response,
            "status": status,
            "timestamp": datetime.now().isoformat()
        }
        
        self.results.append(result)
        print(f"   {status}: {response[:50]}...")
        return result
    
    def evaluate_result(self, expected: str, actual: str) -> str:
        """Evalúa si el resultado es correcto"""
        if "TIMEOUT" in actual:
            return "TIMEOUT"
        elif "ERROR" in actual:
            return "FAIL"
        elif expected.lower() in actual.lower():
            return "PASS"
        elif any(keyword in actual.lower() for keyword in ["error", "no entend", "formato", "inválido"]):
            return "WARNING"
        else:
            return "UNKNOWN"
    
    def save_results(self, filename: str = None):
        """Guarda resultados en archivo CSV"""
        if not filename:
            timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
            filename = f"test_results_{timestamp}.csv"
        
        with open(filename, 'w', newline='', encoding='utf-8') as csvfile:
            fieldnames = ['test_id', 'name', 'input', 'expected', 'actual', 'status', 'timestamp']
            writer = csv.DictWriter(csvfile, fieldnames=fieldnames)
            
            writer.writeheader()
            for result in self.results:
                writer.writerow(result)
        
        print(f"📊 Results saved to: {filename}")
    
    def print_summary(self):
        """Imprime resumen de resultados"""
        total = len(self.results)
        if total == 0:
            print("No test results available")
            return
        
        passed = len([r for r in self.results if r['status'] == 'PASS'])
        failed = len([r for r in self.results if r['status'] == 'FAIL'])
        warnings = len([r for r in self.results if r['status'] == 'WARNING'])
        timeouts = len([r for r in self.results if r['status'] == 'TIMEOUT'])
        unknown = len([r for r in self.results if r['status'] == 'UNKNOWN'])
        
        print("\n" + "="*50)
        print("📊 TEST SUMMARY")
        print("="*50)
        print(f"Total Tests: {total}")
        print(f"✅ PASS:    {passed} ({passed/total*100:.1f}%)")
        print(f"❌ FAIL:    {failed} ({failed/total*100:.1f}%)")
        print(f"⚠️  WARNING: {warnings} ({warnings/total*100:.1f}%)")
        print(f"⏱️  TIMEOUT: {timeouts} ({timeouts/total*100:.1f}%)")
        print(f"❓ UNKNOWN: {unknown} ({unknown/total*100:.1f}%)")
        print("="*50)

# Casos de prueba definidos
TEST_CASES = [
    # INTERPRETACIÓN DE IA - Casos Básicos
    {
        "id": "TC-001-01",
        "name": "Caso básico completo",
        "input": "2 CERS mañana 14hs Hospital Italiano Dr. García",
        "expected": "confirmás estos datos"
    },
    {
        "id": "TC-001-02", 
        "name": "Apendicectomía con fecha absoluta",
        "input": "Apendicectomía 15/08/2025 16:30 Sanatorio Anchorena Dr. López",
        "expected": "confirmás estos datos"
    },
    {
        "id": "TC-001-03",
        "name": "Fecha relativa - hoy",
        "input": "3 adenoides hoy 10hs Clínica Santa Isabel Dr. Martínez", 
        "expected": "confirmás estos datos"
    },
    
    # ERRORES TIPOGRÁFICOS
    {
        "id": "TC-002-01",
        "name": "Errores tipográficos múltiples",
        "input": "2 SERC mañana 14sh Hospital Itlaiano Dr. Garsia",
        "expected": "CERS"  # Esperamos que corrija
    },
    {
        "id": "TC-002-02",
        "name": "Apendicectomía mal escrita",
        "input": "Apendisectomia 15/08 Sandatorio Ancorena",
        "expected": "Apendicectomía"
    },
    
    # CASOS AMBIGUOS
    {
        "id": "TC-003-01",
        "name": "Información incompleta - solo cantidad",
        "input": "2 cirugías mañana",
        "expected": "tipo de cirugía"
    },
    {
        "id": "TC-003-02",
        "name": "Solo tipo y lugar",
        "input": "CERS Hospital",
        "expected": "fecha"
    },
    {
        "id": "TC-003-03",
        "name": "Muy vago",
        "input": "Mañana algo con López",
        "expected": "más específica"
    },
    
    # INFORMACIÓN CONTRADICTORIA
    {
        "id": "TC-004-01",
        "name": "Cantidades contradictorias",
        "input": "2 CERS y 3 CERS mañana 14hs",
        "expected": "cantidad"
    },
    {
        "id": "TC-004-02",
        "name": "Fechas contradictorias",
        "input": "Mañana ayer 14hs Hospital Italiano",
        "expected": "fecha"
    },
    
    # REPORTES
    {
        "id": "TC-201-01",
        "name": "Reporte semanal",
        "input": "/semanal",
        "expected": "Generando reporte semanal"
    },
    {
        "id": "TC-201-02",
        "name": "Reporte mensual",
        "input": "/mensual",
        "expected": "qué mes querés"
    },
    
    # EDGE CASES
    {
        "id": "TC-301-01",
        "name": "Caracteres especiales",
        "input": "CERS con Dr. José María Ñoñez mañana 14hs",
        "expected": "confirmás"
    },
    {
        "id": "TC-301-02",
        "name": "Emojis en texto",
        "input": "Dr. García 😊 Hospital 🏥 CERS mañana 14hs",
        "expected": "confirmás"
    },
    
    # SEGURIDAD
    {
        "id": "TC-303-01",
        "name": "SQL Injection attempt",
        "input": "'; DROP TABLE appointments; --",
        "expected": "no parece ser información"
    },
    {
        "id": "TC-303-02",
        "name": "HTML/Script injection",
        "input": "<script>alert('hack')</script> CERS mañana",
        "expected": "confirmás"
    }
]

def main():
    """Función principal"""
    print("🤖 RegistroCx Bot Automated Testing")
    print("="*50)
    
    # Configuración (obtener de variables de entorno)
    bot_token = os.getenv('TELEGRAM_BOT_TOKEN')
    chat_id = os.getenv('TEST_CHAT_ID')
    
    if not bot_token or not chat_id:
        print("❌ Error: Se requieren variables de entorno:")
        print("   TELEGRAM_BOT_TOKEN - Token del bot")
        print("   TEST_CHAT_ID - ID del chat de prueba")
        return
    
    # Inicializar tester
    tester = BotTester(bot_token, chat_id)
    
    print(f"🎯 Ejecutando {len(TEST_CASES)} casos de prueba...")
    print(f"📱 Chat ID: {chat_id}")
    print("-" * 50)
    
    # Ejecutar todos los casos de prueba
    for test_case in TEST_CASES:
        try:
            tester.run_test_case(test_case)
            time.sleep(3)  # Pausa entre tests para no saturar
        except KeyboardInterrupt:
            print("\n⏹️  Testing interrumpido por usuario")
            break
        except Exception as e:
            print(f"❌ Error en test {test_case['id']}: {e}")
    
    # Mostrar resumen y guardar resultados
    tester.print_summary()
    tester.save_results()
    
    print("\n✅ Testing completado!")

if __name__ == "__main__":
    main()