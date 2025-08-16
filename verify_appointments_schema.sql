-- Script para verificar la estructura actual de la tabla appointments
-- Ejecuta esto primero para ver qué columnas existen realmente

SELECT 
    column_name,
    data_type,
    is_nullable,
    column_default
FROM information_schema.columns 
WHERE table_name = 'appointments' 
ORDER BY ordinal_position;

-- Verificar columnas específicas que usamos en los índices
SELECT 
    column_name,
    CASE 
        WHEN COUNT(*) > 0 THEN 'EXISTS' 
        ELSE 'MISSING' 
    END as status
FROM information_schema.columns 
WHERE table_name = 'appointments' 
  AND column_name IN (
    'chat_id',
    'fecha_hora', 
    'cirujano',
    'lugar',
    'cirugia',
    'anestesiologo',
    'calendar_event_id',
    'google_email',
    'reminder_sent',
    'reminder_sent_at'
  )
GROUP BY column_name
ORDER BY column_name;