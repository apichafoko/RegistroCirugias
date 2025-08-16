-- Agregar campo id autonumérico como PRIMARY KEY a user_profiles
-- y hacer chat_id nullable

BEGIN;

-- 1. Eliminar foreign keys temporalmente
ALTER TABLE appointments DROP CONSTRAINT IF EXISTS appointments_chat_id_fkey;
ALTER TABLE surgery_events DROP CONSTRAINT IF EXISTS surgery_events_chat_id_fkey;

-- 2. Eliminar primary key actual si existe
ALTER TABLE user_profiles DROP CONSTRAINT IF EXISTS user_profiles_pkey;

-- 3. Agregar campo id autonumérico
ALTER TABLE user_profiles ADD COLUMN id SERIAL;

-- 4. Hacer chat_id nullable
ALTER TABLE user_profiles ALTER COLUMN chat_id DROP NOT NULL;

-- 5. Establecer id como nueva primary key
ALTER TABLE user_profiles ADD PRIMARY KEY (id);

-- 6. Crear constraint único en chat_id para foreign keys
ALTER TABLE user_profiles 
ADD CONSTRAINT user_profiles_chat_id_unique 
UNIQUE (chat_id);

-- 7. Hacer chat_id nullable en tablas relacionadas
ALTER TABLE appointments ALTER COLUMN chat_id DROP NOT NULL;
ALTER TABLE surgery_events ALTER COLUMN chat_id DROP NOT NULL;

-- 8. Restaurar foreign keys
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