-- Migración para unificar user_profiles y usuarios_telegram
-- Agrega campos de usuarios_telegram a user_profiles y migra los datos

BEGIN;

-- 1. Agregar las columnas faltantes de usuarios_telegram a user_profiles
ALTER TABLE user_profiles 
ADD COLUMN IF NOT EXISTS telegram_user_id BIGINT,
ADD COLUMN IF NOT EXISTS telegram_first_name TEXT,
ADD COLUMN IF NOT EXISTS telegram_username TEXT,
ADD COLUMN IF NOT EXISTS calendar_autorizado BOOLEAN DEFAULT FALSE,
ADD COLUMN IF NOT EXISTS timezone TEXT DEFAULT 'America/Argentina/Buenos_Aires';

-- 2. Migrar datos de usuarios_telegram a user_profiles (si la tabla existe)
DO $$
BEGIN
    -- Verificar si la tabla usuarios_telegram existe
    IF EXISTS (SELECT FROM information_schema.tables WHERE table_name = 'usuarios_telegram') THEN
        
        -- Migrar datos de usuarios_telegram a user_profiles
        UPDATE user_profiles up
        SET 
            telegram_user_id = ut.telegram_id,
            telegram_first_name = ut.nombre,
            telegram_username = ut.username,
            phone = COALESCE(ut.telefono, up.phone), -- Unificar en campo phone
            google_email = COALESCE(ut.email, up.google_email), -- Unificar en campo google_email
            calendar_autorizado = ut.calendar_autorizado
        FROM usuarios_telegram ut
        WHERE up.chat_id = ut.chat_id;
        
        -- Insertar usuarios que están solo en usuarios_telegram pero no en user_profiles
        INSERT INTO user_profiles (
            chat_id,
            telegram_user_id,
            telegram_first_name,
            telegram_username,
            phone,
            google_email,
            calendar_autorizado,
            state,
            created_at,
            updated_at
        )
        SELECT 
            ut.chat_id,
            ut.telegram_id,
            ut.nombre,
            ut.username,
            ut.telefono,
            ut.email,
            ut.calendar_autorizado,
            CASE 
                WHEN ut.telefono IS NULL THEN 0  -- NeedPhone
                WHEN ut.email IS NULL THEN 1     -- NeedEmail
                ELSE 2                           -- NeedOAuth
            END,
            NOW(),
            NOW()
        FROM usuarios_telegram ut
        LEFT JOIN user_profiles up ON ut.chat_id = up.chat_id
        WHERE up.chat_id IS NULL;
        
        RAISE NOTICE 'Datos migrados de usuarios_telegram a user_profiles';
    ELSE
        RAISE NOTICE 'Tabla usuarios_telegram no existe, omitiendo migración de datos';
    END IF;
END
$$;

-- 3. Eliminar campos duplicados (ya no necesarios después de la migración)
-- Los campos access_token, refresh_token, etc. ya no se usan - todo está en google_access_token, etc.
ALTER TABLE user_profiles 
DROP COLUMN IF EXISTS access_token,
DROP COLUMN IF EXISTS refresh_token,
DROP COLUMN IF EXISTS token_expiry,
DROP COLUMN IF EXISTS telegram_phone,
DROP COLUMN IF EXISTS telegram_email;

-- 4. Limpiar campos timestamp duplicados (mantener created_at/updated_at)
UPDATE user_profiles 
SET 
    created_at = COALESCE(created_at, NOW()),
    updated_at = COALESCE(updated_at, NOW())
WHERE created_at IS NULL OR updated_at IS NULL;

-- Eliminar columnas timestamp duplicadas (si existen)
-- Las columnas created_utc/updated_utc no existen en esta tabla
-- ALTER TABLE user_profiles 
-- DROP COLUMN IF EXISTS created_utc,
-- DROP COLUMN IF EXISTS updated_utc;

-- 5. Agregar índices para los nuevos campos de Telegram
CREATE INDEX IF NOT EXISTS idx_user_profiles_telegram_user_id ON user_profiles(telegram_user_id);
CREATE INDEX IF NOT EXISTS idx_user_profiles_telegram_username ON user_profiles(telegram_username);

-- 6. Actualizar las tablas de relación de equipos para usar user_profiles unificado
-- Las tablas userprofile_equipos y usuariotelegram_equipos deberían consolidarse

-- Migrar relaciones de usuariotelegram_equipos a userprofile_equipos si existe
DO $$
BEGIN
    IF EXISTS (SELECT FROM information_schema.tables WHERE table_name = 'usuariotelegram_equipos') THEN
        -- Insertar relaciones que no existan ya en userprofile_equipos
        INSERT INTO userprofile_equipos (equipo_id, user_profile_id, rol, fecha_union)
        SELECT DISTINCT
            ute.equipo_id,
            up.id,
            ute.rol,
            ute.fecha_union
        FROM usuariotelegram_equipos ute
        JOIN usuarios_telegram ut ON ute.usuario_telegram_id = ut.id
        JOIN user_profiles up ON ut.chat_id = up.chat_id
        LEFT JOIN userprofile_equipos upe ON upe.equipo_id = ute.equipo_id AND upe.user_profile_id = up.id
        WHERE upe.id IS NULL;
        
        RAISE NOTICE 'Relaciones de equipos migradas de usuariotelegram_equipos a userprofile_equipos';
    END IF;
END
$$;

-- 7. Agregar constraint para evitar duplicados por chat_id
-- Esto asegura que cada chat_id tenga solo un user_profile
CREATE UNIQUE INDEX IF NOT EXISTS idx_user_profiles_chat_id_unique 
ON user_profiles(chat_id) 
WHERE chat_id IS NOT NULL;

COMMIT;

-- IMPORTANTE: Después de verificar que todo funciona correctamente, ejecutar:
DROP TABLE IF EXISTS usuarios_telegram;
DROP TABLE IF EXISTS usuariotelegram_equipos;

-- Para verificar la migración:
SELECT COUNT(*) as total_user_profiles FROM user_profiles;
SELECT COUNT(*) as with_telegram_data FROM user_profiles WHERE telegram_user_id IS NOT NULL;
SELECT COUNT(*) as total_team_relations FROM userprofile_equipos;