-- Migración para permitir telegram_id nulo en usuarios_telegram
-- Permite escenarios donde un usuario tiene email/telefono pero no ha interactuado con Telegram

BEGIN;

-- 1. Hacer telegram_id nullable
ALTER TABLE usuarios_telegram 
ALTER COLUMN telegram_id DROP NOT NULL;

-- 2. Eliminar constraint único en telegram_id si existe
-- (puede causar problemas con valores NULL)
ALTER TABLE usuarios_telegram DROP CONSTRAINT IF EXISTS usuarios_telegram_telegram_id_key;

-- 3. Crear constraint único que permita múltiples NULLs en telegram_id
-- pero mantenga unicidad para valores no-nulos
CREATE UNIQUE INDEX usuarios_telegram_telegram_id_unique 
ON usuarios_telegram (telegram_id) 
WHERE telegram_id IS NOT NULL;

-- 4. Asegurar que chat_id siga siendo único 
-- (esto es importante para nuestros nuevos conflicts)
CREATE UNIQUE INDEX IF NOT EXISTS usuarios_telegram_chat_id_unique 
ON usuarios_telegram (chat_id);

COMMIT;