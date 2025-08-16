-- Recrear tabla user_profiles limpia con Id en primer lugar
-- ADVERTENCIA: Esto eliminará todos los datos existentes

BEGIN;

-- 1. Eliminar foreign keys
ALTER TABLE appointments DROP CONSTRAINT IF EXISTS appointments_chat_id_fkey;
ALTER TABLE surgery_events DROP CONSTRAINT IF EXISTS surgery_events_chat_id_fkey;

-- 2. Limpiar datos de las tablas
TRUNCATE TABLE usuarios_telegram CASCADE;
TRUNCATE TABLE user_profiles CASCADE;

-- 3. Eliminar tabla user_profiles completamente
DROP TABLE IF EXISTS user_profiles CASCADE;

-- 4. Recrear tabla user_profiles con estructura correcta
CREATE TABLE user_profiles (
    id SERIAL PRIMARY KEY,
    chat_id BIGINT UNIQUE,
    phone VARCHAR(40),
    google_email VARCHAR(255),
    state SMALLINT NOT NULL DEFAULT 0,
    access_token TEXT,
    refresh_token TEXT,
    access_expires_utc TIMESTAMP WITH TIME ZONE,
    oauth_provider VARCHAR(30) DEFAULT 'google',
    oauth_state_nonce TEXT,
    google_access_token TEXT,
    google_refresh_token TEXT,
    google_token_expiry TIMESTAMP WITH TIME ZONE,
    oauth_nonce TEXT,
    created_utc TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    updated_utc TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    created_at TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT NOW()
);

-- 5. Crear índices
CREATE INDEX idx_user_profiles_state ON user_profiles (state);
CREATE UNIQUE INDEX user_profiles_chat_id_unique ON user_profiles (chat_id) WHERE chat_id IS NOT NULL;

-- 6. Hacer chat_id nullable en tablas relacionadas
ALTER TABLE appointments ALTER COLUMN chat_id DROP NOT NULL;
ALTER TABLE surgery_events ALTER COLUMN chat_id DROP NOT NULL;

-- 7. Recrear foreign keys
ALTER TABLE appointments
ADD CONSTRAINT appointments_chat_id_fkey 
FOREIGN KEY (chat_id) 
REFERENCES user_profiles(chat_id)
ON DELETE SET NULL;

ALTER TABLE surgery_events
ADD CONSTRAINT surgery_events_chat_id_fkey 
FOREIGN KEY (chat_id) 
REFERENCES user_profiles(chat_id)
ON DELETE CASCADE;

COMMIT;