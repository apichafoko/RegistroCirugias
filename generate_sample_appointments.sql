-- ================================================
-- GENERADOR DE APPOINTMENTS DE MUESTRA
-- ================================================
-- 
-- INSTRUCCIONES DE USO:
-- 1. Cambiar las variables @TARGET_MONTH y @TARGET_YEAR según necesites
-- 2. Ejecutar todo el script
-- 3. Se generarán appointments distribuidos a lo largo del mes
--
-- ================================================

-- VARIABLES CONFIGURABLES
-- Cambiar solo estas líneas para generar datos de otros meses/años
DO $$
DECLARE
    TARGET_MONTH INTEGER := 7;    -- Mes (1-12) - CAMBIAR AQUÍ
    TARGET_YEAR INTEGER := 2025;  -- Año - CAMBIAR AQUÍ
    EQUIPO_ELEGIDO_ID INTEGER := 1;       -- ID del equipo
    
    -- Variables internas
    start_date DATE;
    end_date DATE;
    iteration_date DATE;
    appointment_count INTEGER := 0;
    
    -- Arrays para selección aleatoria
    surgeon_names TEXT[];
    anesthesiologist_names TEXT[];
    surgery_types TEXT[];
    locations TEXT[];
    
    -- Variables para cada appointment
    selected_surgeon TEXT;
    selected_anesthesiologist TEXT;
    selected_surgery TEXT;
    selected_location TEXT;
    selected_hour INTEGER;
    selected_minute INTEGER;
    appointment_datetime TIMESTAMP;
    user_profile_id INTEGER;
    google_email TEXT;
BEGIN
    -- Calcular fechas del mes
    start_date := (TARGET_YEAR || '-' || LPAD(TARGET_MONTH::TEXT, 2, '0') || '-01')::DATE;
    end_date := (start_date + INTERVAL '1 month' - INTERVAL '1 day')::DATE;
    
    RAISE NOTICE 'Generando appointments para % (% al %)', 
        TO_CHAR(start_date, 'FMMonth YYYY'), start_date, end_date;
    
    -- Obtener cirujanos del equipo
    SELECT ARRAY_AGG(CONCAT(c.nombre, ' ', c.apellido))
    INTO surgeon_names
    FROM cirujanos c
    WHERE c.equipo_id = EQUIPO_ELEGIDO_ID;
    
    -- Obtener anestesiólogos del equipo  
    SELECT ARRAY_AGG(CONCAT(a.nombre, ' ', a.apellido))
    INTO anesthesiologist_names
    FROM anestesiologos a
    WHERE a.equipo_id = EQUIPO_ELEGIDO_ID;
    
    -- Obtener nombres de tipos de cirugía (IDs 13-24)
    SELECT ARRAY_AGG(tc.nombre)
    INTO surgery_types
    FROM cirugias tc
    WHERE tc.id BETWEEN 13 AND 24;
    
    -- Obtener nombres de lugares (IDs 25-48)
    SELECT ARRAY_AGG(l.nombre)
    INTO locations
    FROM lugares l
    WHERE l.id BETWEEN 25 AND 48;
    
    -- Obtener un user_profile_id y google_email del equipo
    SELECT up.id, up.google_email
    INTO user_profile_id, google_email
    FROM user_profiles up
    INNER JOIN user_profile_equipos upe ON up.id = upe.user_profile_id
    WHERE upe.equipo_id = EQUIPO_ELEGIDO_ID
    LIMIT 1;
    
    IF user_profile_id IS NULL THEN
        RAISE EXCEPTION 'No se encontró user_profile para equipo_id %', EQUIPO_ELEGIDO_ID;
    END IF;
    
    -- Generar appointments distribuidos a lo largo del mes
    iteration_date := start_date;
    
    WHILE iteration_date <= end_date LOOP
        -- Saltar fines de semana (opcional)
        IF EXTRACT(DOW FROM iteration_date) NOT IN (0, 6) THEN -- 0=Domingo, 6=Sábado
            
            -- Generar 1-3 appointments por día laboral (aleatorio)
            FOR i IN 1..(1 + FLOOR(RANDOM() * 3))::INTEGER LOOP
                
                -- Seleccionar datos aleatorios
                selected_surgeon := surgeon_names[1 + FLOOR(RANDOM() * array_length(surgeon_names, 1))];
                selected_anesthesiologist := anesthesiologist_names[1 + FLOOR(RANDOM() * array_length(anesthesiologist_names, 1))];
                selected_surgery := surgery_types[1 + FLOOR(RANDOM() * array_length(surgery_types, 1))];
                selected_location := locations[1 + FLOOR(RANDOM() * array_length(locations, 1))];
                
                -- Generar hora aleatoria entre 8:00 y 18:00
                selected_hour := 8 + FLOOR(RANDOM() * 11)::INTEGER; -- 8-18
                selected_minute := (FLOOR(RANDOM() * 4) * 15)::INTEGER; -- 0, 15, 30, 45
                
                appointment_datetime := iteration_date + (selected_hour || ' hours ' || selected_minute || ' minutes')::INTERVAL;
                
                -- Insertar appointment
                INSERT INTO appointments (
                    equipo_id,
                    user_profile_id, 
                    google_email,
                    fecha_hora,
                    lugar,
                    cirujano,
                    cirugia,
                    cantidad,
                    anestesiologo,
                    calendar_event_id,
                    calendar_synced_at,
                    reminder_sent_at,
                    created_at
                ) VALUES (
                    EQUIPO_ELEGIDO_ID,
                    user_profile_id,
                    google_email,
                    appointment_datetime,
                    selected_location,
                    selected_surgeon,
                    selected_surgery,
                    1 + FLOOR(RANDOM() * 4)::INTEGER, -- Cantidad 1-4
                    selected_anesthesiologist,
                    'sample_event_' || appointment_count, -- Calendar event ID simulado
                    appointment_datetime - INTERVAL '1 hour', -- Simulado como sincronizado
                    CASE 
                        WHEN appointment_datetime < NOW() THEN appointment_datetime - INTERVAL '24 hours'
                        ELSE NULL 
                    END, -- Reminder solo para citas pasadas
                    appointment_datetime - INTERVAL '2 days' -- Created 2 días antes
                );
                
                appointment_count := appointment_count + 1;
                
            END LOOP;
        END IF;
        
        iteration_date := iteration_date + INTERVAL '1 day';
    END LOOP;
    
    RAISE NOTICE 'Se generaron % appointments para %/%', appointment_count, TARGET_MONTH, TARGET_YEAR;
    
END $$;

-- ================================================
-- VERIFICACIÓN DE DATOS GENERADOS
-- ================================================
-- Ejecutar esta query para verificar los datos generados:
/*
SELECT 
    DATE(fecha_hora) as fecha,
    COUNT(*) as cantidad_appointments,
    STRING_AGG(DISTINCT cirujano, ', ') as cirujanos_del_dia,
    STRING_AGG(DISTINCT cirugia, ', ') as cirugias_del_dia,
    STRING_AGG(DISTINCT lugar, ', ') as lugares_del_dia
FROM appointments 
WHERE equipo_id = 1 
  AND EXTRACT(MONTH FROM fecha_hora) = 7 
  AND EXTRACT(YEAR FROM fecha_hora) = 2025
GROUP BY DATE(fecha_hora)
ORDER BY fecha;
*/

-- ================================================
-- LIMPIEZA (OPCIONAL)
-- ================================================ 
-- Para borrar los datos generados si necesitas empezar de nuevo:
/*
DELETE FROM appointments 
WHERE equipo_id = 1 
  AND EXTRACT(MONTH FROM fecha_hora) = 7 
  AND EXTRACT(YEAR FROM fecha_hora) = 2025
  AND calendar_event_id LIKE 'sample_event_%';
*/