# 🧪 Casos de Prueba Específicos - RegistroCx

## 🎯 **Instrucciones de Testing**

### **Formato de Resultado:**
Para cada caso, documenta:
- ✅ **PASS**: Funciona correctamente
- ❌ **FAIL**: Error encontrado
- ⚠️ **WARNING**: Comportamiento extraño pero no crítico

### **Información a Capturar:**
- Input exacto enviado
- Output recibido
- Comportamiento esperado vs real
- Logs de error (si los hay)

---

## 🤖 **CATEGORIA 1: INTERPRETACIÓN DE IA**

### **TC-001: Casos Base Funcionales**
```
Input: "2 CERS mañana 14hs Hospital Italiano Dr. García"
Esperado: ✅ Parse correcto de todos los campos
Resultado: [ ] ✅ PASS [ ] ❌ FAIL [ ] ⚠️ WARNING
Notas: ________________________________
```

```
Input: "Apendicectomía 15/08/2025 16:30 Sanatorio Anchorena Dr. López"
Esperado: ✅ Parse completo con fecha absoluta
Resultado: [ ] ✅ PASS [ ] ❌ FAIL [ ] ⚠️ WARNING
Notas: ________________________________
```

```
Input: "3 adenoides hoy 10hs Clínica Santa Isabel Dr. Martínez"
Esperado: ✅ Parse correcto con fecha relativa
Resultado: [ ] ✅ PASS [ ] ❌ FAIL [ ] ⚠️ WARNING
Notas: ________________________________
```

### **TC-002: Errores Tipográficos**
```
Input: "2 SERC mañana 14sh Hospital Itlaiano Dr. Garsia"
Esperado: ✅ Correción automática: CERS, 14hs, Italiano, García
Resultado: [ ] ✅ PASS [ ] ❌ FAIL [ ] ⚠️ WARNING
Notas: ________________________________
```

```
Input: "Apendisectomia 15/08 Sandatorio Ancorena"
Esperado: ✅ Corrección: Apendicectomía, Sanatorio, Anchorena
Resultado: [ ] ✅ PASS [ ] ❌ FAIL [ ] ⚠️ WARNING
Notas: ________________________________
```

### **TC-003: Casos Ambiguos**
```
Input: "2 cirugías mañana"
Esperado: ✅ Pide tipo de cirugía específico
Resultado: [ ] ✅ PASS [ ] ❌ FAIL [ ] ⚠️ WARNING
Notas: ________________________________
```

```
Input: "CERS Hospital"
Esperado: ✅ Pide fecha, hora y nombre de hospital
Resultado: [ ] ✅ PASS [ ] ❌ FAIL [ ] ⚠️ WARNING
Notas: ________________________________
```

```
Input: "Mañana algo con López"
Esperado: ✅ Pide información más específica
Resultado: [ ] ✅ PASS [ ] ❌ FAIL [ ] ⚠️ WARNING
Notas: ________________________________
```

### **TC-004: Información Contradictoria**
```
Input: "2 CERS y 3 CERS mañana 14hs"
Esperado: ✅ Pide clarificación sobre cantidad (2 o 3)
Resultado: [ ] ✅ PASS [ ] ❌ FAIL [ ] ⚠️ WARNING
Notas: ________________________________
```

```
Input: "Mañana ayer 14hs Hospital Italiano"
Esperado: ✅ Pide clarificación sobre fecha (mañana o ayer)
Resultado: [ ] ✅ PASS [ ] ❌ FAIL [ ] ⚠️ WARNING
Notas: ________________________________
```

### **TC-005: Casos Extremos**
```
Input: "CERS CERS CERS mañana mañana 14hs 14hs Hospital Hospital"
Esperado: ✅ Normaliza duplicados correctamente
Resultado: [ ] ✅ PASS [ ] ❌ FAIL [ ] ⚠️ WARNING
Notas: ________________________________
```

```
Input: "no no no cirugía si si mañana"
Esperado: ✅ Maneja negaciones/afirmaciones conflictivas
Resultado: [ ] ✅ PASS [ ] ❌ FAIL [ ] ⚠️ WARNING
Notas: ________________________________
```

---

## 🔄 **CATEGORIA 2: FLUJOS DE ESTADO**

### **TC-101: Flujo Normal Completo**
```
Paso 1: Enviar "CERS mañana" (incompleto)
Esperado: ✅ Pide información faltante
Resultado: [ ] ✅ PASS [ ] ❌ FAIL [ ] ⚠️ WARNING

Paso 2: Responder "14hs"
Esperado: ✅ Acepta hora, pide siguiente campo
Resultado: [ ] ✅ PASS [ ] ❌ FAIL [ ] ⚠️ WARNING

Paso 3: Responder "Hospital Italiano"
Esperado: ✅ Acepta lugar, pide cirujano
Resultado: [ ] ✅ PASS [ ] ❌ FAIL [ ] ⚠️ WARNING

Paso 4: Responder "Dr. García"
Esperado: ✅ Pide confirmación final
Resultado: [ ] ✅ PASS [ ] ❌ FAIL [ ] ⚠️ WARNING

Paso 5: Responder "sí"
Esperado: ✅ Guarda en BD y sincroniza calendar
Resultado: [ ] ✅ PASS [ ] ❌ FAIL [ ] ⚠️ WARNING
```

### **TC-102: Interrupciones del Flujo**
```
Paso 1: Enviar "CERS mañana" (incompleto)
Paso 2: En lugar de completar, enviar "/start"
Esperado: ✅ Reinicia flujo, limpia estado anterior
Resultado: [ ] ✅ PASS [ ] ❌ FAIL [ ] ⚠️ WARNING
Notas: ________________________________
```

```
Paso 1: Enviar datos incompletos
Paso 2: Enviar "/mensual" en mitad del wizard
Esperado: ✅ Maneja comando de reporte correctamente
Resultado: [ ] ✅ PASS [ ] ❌ FAIL [ ] ⚠️ WARNING
Notas: ________________________________
```

### **TC-103: Anestesiólogo Opcional**
```
Paso 1: Completar todos los campos básicos
Paso 2: Sistema pregunta "¿Asignar anestesiólogo?"
Paso 3: Responder "No"
Esperado: ✅ Guarda sin anestesiólogo (NULL en BD)
Resultado: [ ] ✅ PASS [ ] ❌ FAIL [ ] ⚠️ WARNING
Notas: ________________________________
```

```
Paso 1: Completar todos los campos básicos
Paso 2: Sistema pregunta "¿Asignar anestesiólogo?"
Paso 3: Responder "Sí"
Paso 4: Escribir "Dr. López"
Esperado: ✅ Busca y asigna anestesiólogo
Resultado: [ ] ✅ PASS [ ] ❌ FAIL [ ] ⚠️ WARNING
Notas: ________________________________
```

---

## 📊 **CATEGORIA 3: REPORTES**

### **TC-201: Reportes Básicos**
```
Input: "/semanal"
Esperado: ✅ Genera reporte de últimos 7 días
Resultado: [ ] ✅ PASS [ ] ❌ FAIL [ ] ⚠️ WARNING
Notas: ________________________________
```

```
Input: "/mensual" -> "07/2025"
Esperado: ✅ Genera reporte de julio 2025
Resultado: [ ] ✅ PASS [ ] ❌ FAIL [ ] ⚠️ WARNING
Notas: ________________________________
```

### **TC-202: Reportes con Datos Extremos**
```
Setup: BD sin cirugías
Input: "/semanal"
Esperado: ✅ Reporte con mensaje "Sin datos"
Resultado: [ ] ✅ PASS [ ] ❌ FAIL [ ] ⚠️ WARNING
Notas: ________________________________
```

```
Setup: BD con 1000+ cirugías en un mes
Input: "/mensual" -> "12/2024"
Esperado: ✅ Genera reporte sin timeouts
Resultado: [ ] ✅ PASS [ ] ❌ FAIL [ ] ⚠️ WARNING
Notas: ________________________________
```

### **TC-203: Errores de Fecha en Reportes**
```
Input: "/mensual" -> "15/2025" (mes inválido)
Esperado: ✅ Error claro pidiendo formato MM/YYYY
Resultado: [ ] ✅ PASS [ ] ❌ FAIL [ ] ⚠️ WARNING
Notas: ________________________________
```

```
Input: "/mensual" -> "02/2030" (futuro)
Esperado: ✅ Error o reporte vacío apropiado
Resultado: [ ] ✅ PASS [ ] ❌ FAIL [ ] ⚠️ WARNING
Notas: ________________________________
```

---

## 🚨 **CATEGORIA 4: EDGE CASES EXTREMOS**

### **TC-301: Límites de Texto**
```
Input: [Mensaje de 4000+ caracteres con datos de cirugía]
Esperado: ⚠️ Procesa o da error claro sobre límite
Resultado: [ ] ✅ PASS [ ] ❌ FAIL [ ] ⚠️ WARNING
Notas: ________________________________
```

### **TC-302: Caracteres Especiales**
```
Input: "CERS con Dr. José María Ñoñez en Clínica São Paulo"
Esperado: ✅ Maneja acentos y eñes correctamente
Resultado: [ ] ✅ PASS [ ] ❌ FAIL [ ] ⚠️ WARNING
Notas: ________________________________
```

```
Input: "Dr. García 😊 Hospital 🏥 mañana"
Esperado: ✅ Ignora emojis, procesa texto
Resultado: [ ] ✅ PASS [ ] ❌ FAIL [ ] ⚠️ WARNING
Notas: ________________________________
```

### **TC-303: Ataques de Seguridad**
```
Input: "'; DROP TABLE appointments; --"
Esperado: ✅ No ejecuta SQL, trata como texto normal
Resultado: [ ] ✅ PASS [ ] ❌ FAIL [ ] ⚠️ WARNING
Notas: ________________________________
```

```
Input: "<script>alert('hack')</script> CERS mañana"
Esperado: ✅ Escapa HTML, procesa cirugía
Resultado: [ ] ✅ PASS [ ] ❌ FAIL [ ] ⚠️ WARNING
Notas: ________________________________
```

---

## 🎤 **CATEGORIA 5: TESTING DE AUDIO**

### **TC-401: Audio Normal**
```
Input: [Audio claro diciendo "2 CERS mañana 14hs Hospital Italiano"]
Esperado: ✅ Transcripción perfecta
Resultado: [ ] ✅ PASS [ ] ❌ FAIL [ ] ⚠️ WARNING
Notas: ________________________________
```

### **TC-402: Audio con Problemas**
```
Input: [Audio con mucho ruido de fondo]
Esperado: ⚠️ Transcripción parcial o error claro
Resultado: [ ] ✅ PASS [ ] ❌ FAIL [ ] ⚠️ WARNING
Notas: ________________________________
```

```
Input: [Audio muy bajito, casi inaudible]
Esperado: ⚠️ Error o pide repetir más fuerte
Resultado: [ ] ✅ PASS [ ] ❌ FAIL [ ] ⚠️ WARNING
Notas: ________________________________
```

---

## 📋 **HOJA DE RESULTADOS**

### **Resumen de Errores Encontrados:**
| Categoría | Total Tests | Pass | Fail | Warning |
|-----------|-------------|------|------|---------|
| Interpretación IA | | | | |
| Flujos Estado | | | | |
| Reportes | | | | |
| Edge Cases | | | | |
| Audio | | | | |
| **TOTAL** | | | | |

### **Errores Críticos (FAIL):**
1. ________________________________
2. ________________________________
3. ________________________________

### **Warnings Importantes:**
1. ________________________________
2. ________________________________
3. ________________________________

### **Recomendaciones:**
________________________________
________________________________
________________________________