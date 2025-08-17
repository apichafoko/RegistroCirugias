-- ================================================
-- SCRIPT DE MIGRACIÓN A SISTEMA DE EQUIPOS
-- ================================================
-- Este script migra los datos existentes al nuevo sistema de equipos
-- EJECUTAR DESPUÉS de crear el esquema con create_equipos_schema.sql

-- 1. Obtener el ID del equipo principal (debe existir)
DO $$
DECLARE 
    equipo_principal_id INTEGER;
BEGIN
    -- Buscar el equipo principal
    SELECT id INTO equipo_principal_id FROM equipos WHERE nombre = 'Equipo Principal';
    
    IF equipo_principal_id IS NULL THEN
        RAISE EXCEPTION 'No se encontró el Equipo Principal. Ejecutar create_equipos_schema.sql primero.';
    END IF;
    
    RAISE NOTICE 'Equipo Principal ID: %', equipo_principal_id;
END $$;

-- 2. Migrar todos los user_profiles al equipo principal
INSERT INTO user_profile_equipos (user_profile_id, equipo_id, rol)
SELECT 
    up.id,
    (SELECT id FROM equipos WHERE nombre = 'Equipo Principal'),
    'admin' -- Todos los usuarios existentes serán admin inicialmente
FROM user_profiles up
WHERE NOT EXISTS (
    SELECT 1 FROM user_profile_equipos upe 
    WHERE upe.user_profile_id = up.id
);

-- 3. Migrar todos los usuarios_telegram al equipo principal
-- Primero, crear la relación para usuarios que tienen user_profile
INSERT INTO usuario_telegram_equipos (usuario_telegram_id, equipo_id)
SELECT DISTINCT
    ut.id,
    (SELECT id FROM equipos WHERE nombre = 'Equipo Principal')
FROM usuarios_telegram ut
INNER JOIN user_profiles up ON ut.chat_id = up.chat_id
WHERE NOT EXISTS (
    SELECT 1 FROM usuario_telegram_equipos ute 
    WHERE ute.usuario_telegram_id = ut.id
);

-- También migrar usuarios telegram que no tienen user_profile pero están activos
INSERT INTO usuario_telegram_equipos (usuario_telegram_id, equipo_id)
SELECT DISTINCT
    ut.id,
    (SELECT id FROM equipos WHERE nombre = 'Equipo Principal')
FROM usuarios_telegram ut
WHERE NOT EXISTS (
    SELECT 1 FROM user_profiles up WHERE up.chat_id = ut.chat_id
) AND NOT EXISTS (
    SELECT 1 FROM usuario_telegram_equipos ute 
    WHERE ute.usuario_telegram_id = ut.id
);

-- 4. Migrar appointments - actualizar equipo_id basado en chat_id
UPDATE appointments 
SET equipo_id = (SELECT id FROM equipos WHERE nombre = 'Equipo Principal')
WHERE equipo_id IS NULL;

-- 5. Migrar cirujanos - actualizar equipo_id basado en chat_id
UPDATE cirujanos 
SET equipo_id = (SELECT id FROM equipos WHERE nombre = 'Equipo Principal')
WHERE equipo_id IS NULL;

-- 6. Migrar anesthesiologists - actualizar equipo_id basado en chat_id  
UPDATE anesthesiologists 
SET equipo_id = (SELECT id FROM equipos WHERE nombre = 'Equipo Principal')
WHERE equipo_id IS NULL;

-- 7. Verificar la migración
SELECT 'user_profiles migrados' as tabla, COUNT(*) as total 
FROM user_profile_equipos
UNION ALL
SELECT 'usuarios_telegram migrados' as tabla, COUNT(*) as total 
FROM usuario_telegram_equipos
UNION ALL
SELECT 'appointments con equipo_id' as tabla, COUNT(*) as total 
FROM appointments WHERE equipo_id IS NOT NULL
UNION ALL
SELECT 'cirujanos con equipo_id' as tabla, COUNT(*) as total 
FROM cirujanos WHERE equipo_id IS NOT NULL
UNION ALL
SELECT 'anesthesiologists con equipo_id' as tabla, COUNT(*) as total 
FROM anesthesiologists WHERE equipo_id IS NOT NULL;

-- 8. Mostrar resumen del equipo principal
SELECT 
    e.nombre,
    e.email,
    e.estado_suscripcion,
    e.estado,
    COUNT(DISTINCT upe.user_profile_id) as total_users,
    COUNT(DISTINCT ute.usuario_telegram_id) as total_telegram_users,
    COUNT(DISTINCT a.id) as total_appointments
FROM equipos e
LEFT JOIN user_profile_equipos upe ON e.id = upe.equipo_id
LEFT JOIN usuario_telegram_equipos ute ON e.id = ute.equipo_id  
LEFT JOIN appointments a ON e.id = a.equipo_id
WHERE e.nombre = 'Equipo Principal'
GROUP BY e.id, e.nombre, e.email, e.estado_suscripcion, e.estado;

-- 9. OPCIONAL: Comentar/descomentar las siguientes líneas para hacer NOT NULL los campos equipo_id
-- Una vez verificada la migración, se pueden hacer NOT NULL estos campos:

-- ALTER TABLE appointments ALTER COLUMN equipo_id SET NOT NULL;
-- ALTER TABLE cirujanos ALTER COLUMN equipo_id SET NOT NULL;  
-- ALTER TABLE anesthesiologists ALTER COLUMN equipo_id SET NOT NULL;

-- 10. OPCIONAL: Eliminar referencias chat_id después de confirmar que todo funciona
-- Esto se debe hacer más adelante después de actualizar todos los servicios:

-- ALTER TABLE appointments DROP COLUMN chat_id;
-- ALTER TABLE cirujanos DROP COLUMN chat_id;
-- ALTER TABLE anesthesiologists DROP COLUMN chat_id;

COMMIT;