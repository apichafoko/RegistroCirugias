-- Migración para permitir chat_id nulo en user_profiles
-- Agrega un campo id como nueva primary key

BEGIN;

-- 1. Agregar nuevo campo id como serial
ALTER TABLE user_profiles ADD COLUMN id SERIAL;

-- 2. Eliminar las foreign key constraints temporalmente
ALTER TABLE appointments DROP CONSTRAINT IF EXISTS appointments_chat_id_fkey;
ALTER TABLE surgery_events DROP CONSTRAINT IF EXISTS surgery_events_chat_id_fkey;

-- 3. Eliminar la primary key actual
ALTER TABLE user_profiles DROP CONSTRAINT user_profiles_pkey;

-- 4. Hacer chat_id nullable
ALTER TABLE user_profiles ALTER COLUMN chat_id DROP NOT NULL;

-- 5. Establecer id como nueva primary key
ALTER TABLE user_profiles ADD PRIMARY KEY (id);

-- 6. Crear índice único en chat_id para valores no nulos
CREATE UNIQUE INDEX user_profiles_chat_id_unique 
ON user_profiles (chat_id) 
WHERE chat_id IS NOT NULL;

-- NOTA: Las FK fueron eliminadas. Se necesitan ajustes manuales en las tablas
-- appointments y surgery_events si requieren mantener integridad referencial

COMMIT;