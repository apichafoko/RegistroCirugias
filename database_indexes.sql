-- ===================================================================
-- ÍNDICES PARA OPTIMIZACIÓN DE PERFORMANCE
-- Ejecutar después de crear las tablas principales
-- ===================================================================

-- ===================================================================
-- ELIMINAR ÍNDICES EXISTENTES ANTES DE RECREAR
-- ===================================================================

-- Eliminar índices de appointments
DROP INDEX IF EXISTS idx_appointments_user_date;
DROP INDEX IF EXISTS idx_appointments_surgeon;
DROP INDEX IF EXISTS idx_appointments_location;
DROP INDEX IF EXISTS idx_appointments_surgery_type;
DROP INDEX IF EXISTS idx_appointments_date_range;
DROP INDEX IF EXISTS idx_appointments_reminders;
DROP INDEX IF EXISTS idx_appointments_anesthesiologist;
DROP INDEX IF EXISTS idx_appointments_calendar_sync;
DROP INDEX IF EXISTS idx_appointments_google_email;
DROP INDEX IF EXISTS idx_appointments_monthly_reports;
DROP INDEX IF EXISTS idx_appointments_weekly_reports;
DROP INDEX IF EXISTS idx_appointments_surgeon_stats;

-- Eliminar índices de user_profiles
DROP INDEX IF EXISTS idx_user_profiles_phone;
DROP INDEX IF EXISTS idx_user_profiles_email;
DROP INDEX IF EXISTS idx_user_profiles_oauth_active;

-- Eliminar índices de usuarios_telegram
DROP INDEX IF EXISTS idx_usuarios_telegram_user_id;
DROP INDEX IF EXISTS idx_usuarios_telegram_phone;

-- Eliminar índices de anesthesiologists
DROP INDEX IF EXISTS idx_anesthesiologists_fullname;
DROP INDEX IF EXISTS idx_anesthesiologists_email;

-- Eliminar índices de user_learning (si existe)
DROP INDEX IF EXISTS idx_user_learning_user;
DROP INDEX IF EXISTS idx_user_learning_input_pattern;

-- ===================================================================
-- TABLA: appointments
-- ===================================================================

-- Índice compuesto para consultas frecuentes por usuario y fecha
-- Usado en: reportes, búsquedas de appointments por usuario
CREATE INDEX IF NOT EXISTS idx_appointments_user_date 
ON appointments(chat_id, fecha_hora);

-- Índice para búsquedas por cirujano
-- Usado en: reportes por cirujano, búsquedas, estadísticas
CREATE INDEX IF NOT EXISTS idx_appointments_surgeon 
ON appointments(cirujano) 
WHERE cirujano IS NOT NULL;

-- Índice para búsquedas por lugar
-- Usado en: reportes por lugar, estadísticas de lugares
CREATE INDEX IF NOT EXISTS idx_appointments_location 
ON appointments(lugar) 
WHERE lugar IS NOT NULL;

-- Índice para búsquedas por tipo de cirugía
-- Usado en: reportes por tipo, estadísticas de cirugías más frecuentes
CREATE INDEX IF NOT EXISTS idx_appointments_surgery_type 
ON appointments(cirugia) 
WHERE cirugia IS NOT NULL;

-- Índice para appointments por rango de fechas (reportes)
-- Usado en: reportes semanales, mensuales, anuales
-- Nota: Removido WHERE con CURRENT_DATE porque no es inmutable
CREATE INDEX IF NOT EXISTS idx_appointments_date_range 
ON appointments(fecha_hora);

-- Índice para appointments que necesitan recordatorios
-- Usado en: sistema de recordatorios automáticos
-- Nota: Usa reminder_sent_at en lugar de reminder_sent
CREATE INDEX IF NOT EXISTS idx_appointments_reminders 
ON appointments(fecha_hora, reminder_sent_at) 
WHERE reminder_sent_at IS NULL;

-- Índice para búsquedas por anestesiólogo
-- Usado en: invitaciones por email, reportes
CREATE INDEX IF NOT EXISTS idx_appointments_anesthesiologist 
ON appointments(anestesiologo) 
WHERE anestesiologo IS NOT NULL;

-- Índice para sincronización con Google Calendar
-- Usado en: sync de eventos de calendario
CREATE INDEX IF NOT EXISTS idx_appointments_calendar_sync 
ON appointments(calendar_event_id, chat_id) 
WHERE calendar_event_id IS NOT NULL;

-- Índice para appointments por Google Email (equipos compartidos)
-- Usado en: reportes compartidos por equipo
CREATE INDEX IF NOT EXISTS idx_appointments_google_email 
ON appointments(google_email, fecha_hora) 
WHERE google_email IS NOT NULL;

-- ===================================================================
-- TABLA: user_profiles
-- ===================================================================

-- Índice único para búsquedas por teléfono (login por teléfono)
-- Usado en: onboarding, identificación de usuarios
CREATE UNIQUE INDEX IF NOT EXISTS idx_user_profiles_phone 
ON user_profiles(phone) 
WHERE phone IS NOT NULL;

-- Índice para búsquedas por email (equipos compartidos)
-- Usado en: compartir tokens OAuth entre usuarios del mismo equipo
CREATE INDEX IF NOT EXISTS idx_user_profiles_email 
ON user_profiles(google_email) 
WHERE google_email IS NOT NULL;

-- Índice para usuarios activos (tienen tokens OAuth)
-- Usado en: validación de usuarios autorizados
CREATE INDEX IF NOT EXISTS idx_user_profiles_oauth_active 
ON user_profiles(google_email, state) 
WHERE google_access_token IS NOT NULL;

-- ===================================================================
-- TABLA: usuarios_telegram
-- ===================================================================

-- Índice para búsquedas por telegram_user_id
-- Usado en: identificación de usuarios por ID de Telegram
CREATE INDEX IF NOT EXISTS idx_usuarios_telegram_user_id 
ON usuarios_telegram(telegram_user_id) 
WHERE telegram_user_id IS NOT NULL;

-- Índice para búsquedas por teléfono (vinculación con user_profiles)
-- Usado en: actualización de datos de Telegram por teléfono
CREATE INDEX IF NOT EXISTS idx_usuarios_telegram_phone 
ON usuarios_telegram(phone) 
WHERE phone IS NOT NULL;

-- ===================================================================
-- TABLA: anesthesiologists
-- ===================================================================

-- Índice para búsquedas por nombre completo
-- Usado en: búsqueda de emails de anestesiólogos
CREATE INDEX IF NOT EXISTS idx_anesthesiologists_fullname 
ON anesthesiologists(nombre, apellido);

-- Índice para búsquedas por email
-- Usado en: verificación de emails existentes
CREATE UNIQUE INDEX IF NOT EXISTS idx_anesthesiologists_email 
ON anesthesiologists(email);

-- ===================================================================
-- TABLA: user_learning (si existe)
-- ===================================================================

-- Índice para aprendizaje por usuario
-- Usado en: sistema de aprendizaje personalizado
CREATE INDEX IF NOT EXISTS idx_user_learning_user 
ON user_learning(chat_id, created_at) 
WHERE chat_id IS NOT NULL;

-- Índice para búsquedas de patrones de input
-- Usado en: mejora del parsing basado en historial
CREATE INDEX IF NOT EXISTS idx_user_learning_input_pattern 
ON user_learning(input_text) 
WHERE input_text IS NOT NULL;

-- ===================================================================
-- ÍNDICES PARA PERFORMANCE DE REPORTES
-- ===================================================================

-- Índice compuesto para reportes mensuales eficientes
CREATE INDEX IF NOT EXISTS idx_appointments_monthly_reports 
ON appointments(google_email, EXTRACT(YEAR FROM fecha_hora), EXTRACT(MONTH FROM fecha_hora), cirugia);

-- Índice compuesto para reportes semanales eficientes  
CREATE INDEX IF NOT EXISTS idx_appointments_weekly_reports 
ON appointments(google_email, EXTRACT(WEEK FROM fecha_hora), EXTRACT(YEAR FROM fecha_hora), cirujano);

-- Índice para estadísticas de cirujanos más activos
-- Nota: Removido WHERE con fecha relativa porque no es inmutable
CREATE INDEX IF NOT EXISTS idx_appointments_surgeon_stats 
ON appointments(cirujano, fecha_hora) 
WHERE cirujano IS NOT NULL;

-- ===================================================================
-- MANTENIMIENTO DE ÍNDICES
-- ===================================================================

-- Comando para analizar el uso de índices (ejecutar periódicamente):
-- SELECT schemaname, tablename, indexname, idx_tup_read, idx_tup_fetch 
-- FROM pg_stat_user_indexes 
-- WHERE schemaname = 'public' 
-- ORDER BY idx_tup_read DESC;

-- Comando para encontrar índices no utilizados:
-- SELECT schemaname, tablename, indexname 
-- FROM pg_stat_user_indexes 
-- WHERE idx_tup_read = 0 AND idx_tup_fetch = 0 
-- AND schemaname = 'public';