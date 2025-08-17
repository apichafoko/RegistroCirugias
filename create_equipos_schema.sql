-- ================================================
-- CREACIÓN DE ESQUEMA DE EQUIPOS
-- ================================================

-- 1. Crear tabla Equipos
CREATE TABLE IF NOT EXISTS equipos (
    id SERIAL PRIMARY KEY,
    nombre VARCHAR(255) NOT NULL UNIQUE,
    email VARCHAR(255) NOT NULL UNIQUE,
    estado_suscripcion VARCHAR(50) NOT NULL DEFAULT 'prueba', -- 'prueba', 'pago', 'cancelado', 'vencido'
    fecha_pago TIMESTAMP WITH TIME ZONE,
    fecha_vencimiento TIMESTAMP WITH TIME ZONE,
    estado VARCHAR(20) NOT NULL DEFAULT 'activo', -- 'activo', 'inactivo'
    created_at TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT NOW()
);

-- 2. Crear tabla intermedia UserProfile_Equipos (many-to-many)
CREATE TABLE IF NOT EXISTS user_profile_equipos (
    id SERIAL PRIMARY KEY,
    user_profile_id INTEGER NOT NULL,
    equipo_id INTEGER NOT NULL,
    rol VARCHAR(50) DEFAULT 'miembro', -- 'admin', 'miembro', 'viewer'
    created_at TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    FOREIGN KEY (user_profile_id) REFERENCES user_profiles(id) ON DELETE CASCADE,
    FOREIGN KEY (equipo_id) REFERENCES equipos(id) ON DELETE CASCADE,
    UNIQUE(user_profile_id, equipo_id)
);

-- 3. Crear tabla intermedia UsuarioTelegram_Equipos (many-to-many)
CREATE TABLE IF NOT EXISTS usuario_telegram_equipos (
    id SERIAL PRIMARY KEY,
    usuario_telegram_id INTEGER NOT NULL,
    equipo_id INTEGER NOT NULL,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    FOREIGN KEY (usuario_telegram_id) REFERENCES usuarios_telegram(id) ON DELETE CASCADE,
    FOREIGN KEY (equipo_id) REFERENCES equipos(id) ON DELETE CASCADE,
    UNIQUE(usuario_telegram_id, equipo_id)
);

-- 4. Agregar equipo_id a tabla cirujanos (y mantener chat_id temporalmente para migración)
ALTER TABLE cirujanos 
ADD COLUMN IF NOT EXISTS equipo_id INTEGER,
ADD FOREIGN KEY (equipo_id) REFERENCES equipos(id) ON DELETE CASCADE;

-- 5. Agregar equipo_id a tabla anesthesiologists (y mantener chat_id temporalmente para migración)
ALTER TABLE anesthesiologists 
ADD COLUMN IF NOT EXISTS equipo_id INTEGER,
ADD FOREIGN KEY (equipo_id) REFERENCES equipos(id) ON DELETE CASCADE;

-- 6. Agregar equipo_id a tabla appointments (y mantener chat_id temporalmente para migración)
ALTER TABLE appointments 
ADD COLUMN IF NOT EXISTS equipo_id INTEGER,
ADD FOREIGN KEY (equipo_id) REFERENCES equipos(id) ON DELETE CASCADE;

-- 7. Crear índices para mejorar performance
CREATE INDEX IF NOT EXISTS idx_equipos_email ON equipos(email);
CREATE INDEX IF NOT EXISTS idx_equipos_estado ON equipos(estado);
CREATE INDEX IF NOT EXISTS idx_equipos_estado_suscripcion ON equipos(estado_suscripcion);

CREATE INDEX IF NOT EXISTS idx_user_profile_equipos_user_id ON user_profile_equipos(user_profile_id);
CREATE INDEX IF NOT EXISTS idx_user_profile_equipos_equipo_id ON user_profile_equipos(equipo_id);

CREATE INDEX IF NOT EXISTS idx_usuario_telegram_equipos_usuario_id ON usuario_telegram_equipos(usuario_telegram_id);
CREATE INDEX IF NOT EXISTS idx_usuario_telegram_equipos_equipo_id ON usuario_telegram_equipos(equipo_id);

CREATE INDEX IF NOT EXISTS idx_cirujanos_equipo_id ON cirujanos(equipo_id);
CREATE INDEX IF NOT EXISTS idx_anesthesiologists_equipo_id ON anesthesiologists(equipo_id);
CREATE INDEX IF NOT EXISTS idx_appointments_equipo_id ON appointments(equipo_id);

-- 8. Función para actualizar updated_at automáticamente
CREATE OR REPLACE FUNCTION update_updated_at_column()
RETURNS TRIGGER AS $$
BEGIN
    NEW.updated_at = NOW();
    RETURN NEW;
END;
$$ language 'plpgsql';

-- 9. Trigger para actualizar updated_at en equipos
DROP TRIGGER IF EXISTS update_equipos_updated_at ON equipos;
CREATE TRIGGER update_equipos_updated_at 
    BEFORE UPDATE ON equipos 
    FOR EACH ROW 
    EXECUTE FUNCTION update_updated_at_column();

-- 10. Insertar equipo por defecto para migración de datos existentes
INSERT INTO equipos (nombre, email, estado_suscripcion, estado)
VALUES ('Equipo Principal', 'admin@registrocx.com', 'pago', 'activo')
ON CONFLICT (nombre) DO NOTHING;

-- Comentarios sobre la estructura:
-- - equipos: Tabla principal con información de suscripción y facturación
-- - user_profile_equipos: Relación many-to-many con roles para permisos granulares
-- - usuario_telegram_equipos: Relación many-to-many para asociar chats de Telegram
-- - equipo_id agregado a cirujanos, anesthesiologists y appointments
-- - chat_id se mantiene temporalmente para migración gradual
-- - Índices optimizados para consultas frecuentes
-- - Triggers para auditoria automática