-- Migración para sistema de aprendizaje personalizado del usuario
-- Ejecutar después de las migraciones existentes

-- Tabla para términos personalizados del usuario
CREATE TABLE IF NOT EXISTS user_custom_terms (
    id BIGSERIAL PRIMARY KEY,
    chat_id BIGINT NOT NULL,
    user_term VARCHAR(100) NOT NULL,        -- "cataratas", "faco", "quiroga"
    standard_term VARCHAR(100) NOT NULL,    -- "FACOEMULSIFICACION", "Dr. Andrea Quiroga"
    term_type VARCHAR(50) NOT NULL,         -- "surgery", "surgeon", "place", "anesthesiologist"
    frequency INT DEFAULT 1,                -- Cuántas veces lo usó
    confidence DECIMAL(3,2) DEFAULT 0.5,   -- Confianza de la predicción (0.0 - 1.0)
    first_seen TIMESTAMP DEFAULT NOW(),
    last_used TIMESTAMP DEFAULT NOW(),
    created_at TIMESTAMP DEFAULT NOW(),
    updated_at TIMESTAMP DEFAULT NOW(),
    CONSTRAINT unique_user_term UNIQUE(chat_id, user_term, term_type)
);

-- Tabla para patrones de comunicación del usuario
CREATE TABLE IF NOT EXISTS user_communication_patterns (
    id BIGSERIAL PRIMARY KEY,
    chat_id BIGINT NOT NULL,
    pattern_type VARCHAR(50) NOT NULL,     -- "frequent_surgery", "typical_surgeon", "preferred_place"
    pattern_value TEXT NOT NULL,           -- JSON o string simple
    frequency INT DEFAULT 1,
    confidence DECIMAL(3,2) DEFAULT 0.5,
    last_used TIMESTAMP DEFAULT NOW(),
    created_at TIMESTAMP DEFAULT NOW(),
    updated_at TIMESTAMP DEFAULT NOW(),
    CONSTRAINT unique_user_pattern UNIQUE(chat_id, pattern_type, pattern_value)
);

-- Índices para optimizar consultas
CREATE INDEX IF NOT EXISTS idx_user_custom_terms_chat_id ON user_custom_terms(chat_id);
CREATE INDEX IF NOT EXISTS idx_user_custom_terms_type ON user_custom_terms(chat_id, term_type);
CREATE INDEX IF NOT EXISTS idx_user_custom_terms_frequency ON user_custom_terms(chat_id, frequency DESC);

CREATE INDEX IF NOT EXISTS idx_user_patterns_chat_id ON user_communication_patterns(chat_id);
CREATE INDEX IF NOT EXISTS idx_user_patterns_type ON user_communication_patterns(chat_id, pattern_type);
CREATE INDEX IF NOT EXISTS idx_user_patterns_frequency ON user_communication_patterns(chat_id, frequency DESC);

-- Comentarios para documentación
COMMENT ON TABLE user_custom_terms IS 'Almacena términos personalizados que cada usuario utiliza para cirugías, cirujanos, lugares, etc.';
COMMENT ON TABLE user_communication_patterns IS 'Almacena patrones de comunicación del usuario para predicciones inteligentes';

COMMENT ON COLUMN user_custom_terms.user_term IS 'Término que usa el usuario (ej: "cataratas", "faco")';
COMMENT ON COLUMN user_custom_terms.standard_term IS 'Término estándar correspondiente (ej: "FACOEMULSIFICACION")';
COMMENT ON COLUMN user_custom_terms.term_type IS 'Tipo: surgery, surgeon, place, anesthesiologist';
COMMENT ON COLUMN user_custom_terms.confidence IS 'Confianza de 0.0 a 1.0 basada en frecuencia y contexto';