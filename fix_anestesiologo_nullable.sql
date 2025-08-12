-- Script para corregir el problema de anestesiólogo opcional
-- Permite que la columna anestesiologo sea NULL

-- Paso 1: Cambiar la columna para permitir NULL
ALTER TABLE appointments 
ALTER COLUMN anestesiologo DROP NOT NULL;

-- Paso 2: Actualizar registros existentes que tengan string vacío a NULL (opcional)
UPDATE appointments 
SET anestesiologo = NULL 
WHERE anestesiologo = '' OR anestesiologo = ' ';

-- Paso 3: Verificar el cambio
SELECT column_name, is_nullable, data_type 
FROM information_schema.columns 
WHERE table_name = 'appointments' AND column_name = 'anestesiologo';

-- El resultado debería mostrar is_nullable = 'YES'