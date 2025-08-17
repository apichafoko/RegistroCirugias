# Especificación del Prompt para Análisis de Contexto Conversacional

## Información del Prompt
- **Nombre sugerido**: "Context Analysis - Surgery Bot"
- **Propósito**: Analizar si el mensaje de un usuario es relevante al contexto conversacional actual
- **Prompt ID**: `pmpt_context_analysis_001` (actualizar en código cuando tengas el ID real)
- **Versión**: `1`

## Descripción
Este prompt analiza si el mensaje de un usuario es relevante al contexto actual de una conversación sobre registro de cirugías. Ayuda a detectar cuando el usuario se desvía del tema o cambia de contexto explícitamente.

## Input Structure
El input será un JSON con la siguiente estructura:

```json
{
    "contexto_actual": "esperando_campo_fecha_y_hora | esperando_confirmacion_cirugia | registrando_nueva_cirugia | modificando_cirugia_existente | conversacion_activa",
    "detalle_contexto": "Descripción específica del contexto actual",
    "mensaje_usuario": "El mensaje exacto que envió el usuario",
    "timestamp": "2024-01-15T10:30:00Z"
}
```

### Ejemplos de Input:

**Ejemplo 1 - Mensaje irrelevante:**
```json
{
    "contexto_actual": "esperando_campo_fecha_y_hora",
    "detalle_contexto": "Esperando campo: fecha y hora",
    "mensaje_usuario": "perro verde",
    "timestamp": "2024-01-15T10:30:00Z"
}
```

**Ejemplo 2 - Mensaje relevante:**
```json
{
    "contexto_actual": "esperando_campo_fecha_y_hora",
    "detalle_contexto": "Esperando campo: fecha y hora",
    "mensaje_usuario": "mañana a las 2 de la tarde",
    "timestamp": "2024-01-15T10:30:00Z"
}
```

**Ejemplo 3 - Cambio explícito de contexto:**
```json
{
    "contexto_actual": "esperando_campo_lugar",
    "detalle_contexto": "Esperando campo: lugar/hospital",
    "mensaje_usuario": "cancelar todo quiero empezar de nuevo",
    "timestamp": "2024-01-15T10:30:00Z"
}
```

## Output Structure
El output debe ser un JSON válido con esta estructura exacta:

```json
{
    "relevant": true,
    "confidence": 0.85,
    "reason": "El mensaje contiene información de fecha y hora apropiada para el contexto",
    "context_switch": false
}
```

### Campos del Output:
- **relevant** (boolean): `true` si el mensaje es relevante al contexto actual, `false` si no
- **confidence** (number): Nivel de confianza de 0.0 a 1.0
- **reason** (string): Explicación breve de por qué es relevante o no
- **context_switch** (boolean): `true` si detecta intención explícita de cambiar de contexto

## Criterios de Análisis

### Contextos y qué es relevante para cada uno:

#### `esperando_campo_fecha_y_hora`
- **Relevante**: fechas, horas, días de la semana, "mañana", "hoy", números con "hs", fechas en formato DD/MM
- **Irrelevante**: nombres, lugares, colores, animales, objetos no relacionados

#### `esperando_campo_lugar`
- **Relevante**: nombres de hospitales, clínicas, sanatorios, direcciones
- **Irrelevante**: fechas, horas, nombres de personas, colores, animales

#### `esperando_campo_cirujano`
- **Relevante**: nombres de personas, títulos médicos (Dr., Dra.), apellidos
- **Irrelevante**: fechas, lugares, colores, animales, objetos

#### `esperando_confirmacion_cirugia`
- **Relevante**: "sí", "no", "confirmar", "ok", "dale", "perfecto", "cambiar", "editar"
- **Irrelevante**: datos nuevos de cirugía, colores, animales

### Palabras que indican cambio explícito de contexto:
- "nuevo", "nueva", "empezar", "comenzar", "iniciar", "cancelar", "parar", "salir"
- "modificar", "cambiar", "editar", "reporte", "consulta", "buscar"

### Ejemplos de análisis:

```json
// Input: "perro verde" en contexto esperando_campo_fecha_y_hora
{
    "relevant": false,
    "confidence": 0.95,
    "reason": "Las palabras 'perro verde' no contienen información de fecha u hora",
    "context_switch": false
}

// Input: "mañana 14hs" en contexto esperando_campo_fecha_y_hora
{
    "relevant": true,
    "confidence": 0.9,
    "reason": "Contiene información clara de fecha (mañana) y hora (14hs)",
    "context_switch": false
}

// Input: "cancelar quiero empezar de nuevo" en cualquier contexto
{
    "relevant": false,
    "confidence": 0.98,
    "reason": "Solicitud explícita de cancelar y empezar nuevo proceso",
    "context_switch": true
}

// Input: "Dr. García" en contexto esperando_campo_cirujano
{
    "relevant": true,
    "confidence": 0.9,
    "reason": "Nombre de médico apropiado para el campo cirujano solicitado",
    "context_switch": false
}
```

## Notas Importantes
1. Priorizar detectar cambios explícitos de contexto (`context_switch: true`)
2. Ser estricto con relevancia - mejor falso positivo que perder contexto
3. Confidence alto (>0.8) para casos claros, medio (0.5-0.8) para dudosos
4. Reason debe ser conciso pero explicativo
5. Considerar jerga médica argentina y términos coloquiales

## Integración en el Código
Una vez creado el prompt, actualizar en `ConversationContextManager.cs`:

```csharp
private const string ContextAnalysisPromptId = "TU_PROMPT_ID_REAL";
private const string ContextAnalysisPromptVersion = "1";
```