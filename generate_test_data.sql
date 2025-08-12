-- Test data generation script
-- Generates 50 appointments for July 2025 and 7 for this week
-- Uses existing chat_id and google_email from user_profiles

-- Insert July appointments (50 total)
INSERT INTO appointments 
    (chat_id, google_email, fecha_hora, lugar, cirujano, cirugia, cantidad, anestesiologo, created_at)
WITH existing_users AS (
    SELECT DISTINCT chat_id, google_email 
    FROM user_profiles 
    WHERE google_email IS NOT NULL
    ORDER BY chat_id
    LIMIT 5  -- Limit to first 5 users for test data
)
SELECT 
    eu.chat_id,
    eu.google_email,
    -- Random dates in July 2025
    ('2025-07-' || LPAD((1 + (random() * 30)::int)::text, 2, '0') || ' ' ||
    LPAD((8 + (random() * 10)::int)::text, 2, '0') || ':' ||
    (CASE WHEN random() < 0.5 THEN '00' ELSE '30' END) || ':00')::timestamp AS fecha_hora,
    
    -- Random surgical locations
    (CASE (random() * 4)::int
        WHEN 0 THEN 'Hospital Italiano'
        WHEN 1 THEN 'Sanatorio Finochietto'
        WHEN 2 THEN 'Hospital Alemán'
        ELSE 'Clínica Santa Isabel'
    END) AS lugar,
    
    -- Random surgeons
    (CASE (random() * 5)::int
        WHEN 0 THEN 'Dr. García López'
        WHEN 1 THEN 'Dra. María Rodríguez'
        WHEN 2 THEN 'Dr. Carlos Mendez'
        WHEN 3 THEN 'Dra. Ana Torres'
        ELSE 'Dr. Luis Fernández'
    END) AS cirujano,
    
    -- Random surgery types
    (CASE (random() * 6)::int
        WHEN 0 THEN 'CERS'
        WHEN 1 THEN 'MLD'
        WHEN 2 THEN 'Laparoscopía'
        WHEN 3 THEN 'Colecistectomía'
        WHEN 4 THEN 'Apendicectomía'
        ELSE 'Hernioplastía'
    END) AS cirugia,
    
    -- Random quantities (1-3)
    (1 + (random() * 2)::int) AS cantidad,
    
    -- Random anesthesiologists
    (CASE (random() * 4)::int
        WHEN 0 THEN 'Dr. Pérez'
        WHEN 1 THEN 'Dra. González'
        WHEN 2 THEN 'Dr. Martínez'
        ELSE 'Dra. López'
    END) AS anestesiologo,
    
    now() - interval '1 month' AS created_at -- Created a month ago
FROM existing_users eu
CROSS JOIN generate_series(1, 10) -- 10 appointments per user = 50 total
ORDER BY random()
LIMIT 50;

-- Insert this week appointments (7 total)
INSERT INTO appointments 
    (chat_id, google_email, fecha_hora, lugar, cirujano, cirugia, cantidad, anestesiologo, created_at)
WITH existing_users AS (
    SELECT DISTINCT chat_id, google_email 
    FROM user_profiles 
    WHERE google_email IS NOT NULL
    ORDER BY chat_id
    LIMIT 5
)
SELECT 
    eu.chat_id,
    eu.google_email,
    -- Random dates for this week (August 12-18, 2025)
    ('2025-08-' || LPAD((12 + (random() * 6)::int)::text, 2, '0') || ' ' ||
    LPAD((8 + (random() * 10)::int)::text, 2, '0') || ':' ||
    (CASE WHEN random() < 0.5 THEN '00' ELSE '30' END) || ':00')::timestamp AS fecha_hora,
    
    -- Random locations
    (CASE (random() * 4)::int
        WHEN 0 THEN 'Hospital Italiano'
        WHEN 1 THEN 'Sanatorio Finochietto'
        WHEN 2 THEN 'Hospital Alemán'
        ELSE 'Clínica Santa Isabel'
    END) AS lugar,
    
    -- Random surgeons
    (CASE (random() * 5)::int
        WHEN 0 THEN 'Dr. García López'
        WHEN 1 THEN 'Dra. María Rodríguez'
        WHEN 2 THEN 'Dr. Carlos Mendez'
        WHEN 3 THEN 'Dra. Ana Torres'
        ELSE 'Dr. Luis Fernández'
    END) AS cirujano,
    
    -- Random surgery types
    (CASE (random() * 6)::int
        WHEN 0 THEN 'CERS'
        WHEN 1 THEN 'MLD'
        WHEN 2 THEN 'Laparoscopía'
        WHEN 3 THEN 'Colecistectomía'
        WHEN 4 THEN 'Apendicectomía'
        ELSE 'Hernioplastía'
    END) AS cirugia,
    
    -- Random quantities
    (1 + (random() * 2)::int) AS cantidad,
    
    -- Random anesthesiologists
    (CASE (random() * 4)::int
        WHEN 0 THEN 'Dr. Pérez'
        WHEN 1 THEN 'Dra. González'
        WHEN 2 THEN 'Dr. Martínez'
        ELSE 'Dra. López'
    END) AS anestesiologo,
    
    now() - interval '1 day' AS created_at -- Created yesterday
FROM existing_users eu
CROSS JOIN generate_series(1, 2) -- 2 appointments per user, but we'll limit to 7 total
ORDER BY random()
LIMIT 7;

-- Show summary of inserted data
SELECT 
    'July 2025' as period,
    COUNT(*) as appointments_count,
    MIN(fecha_hora) as earliest_appointment,
    MAX(fecha_hora) as latest_appointment
FROM appointments 
WHERE fecha_hora >= '2025-07-01' AND fecha_hora < '2025-08-01'

UNION ALL

SELECT 
    'This week (Aug 12-18)' as period,
    COUNT(*) as appointments_count,
    MIN(fecha_hora) as earliest_appointment,
    MAX(fecha_hora) as latest_appointment
FROM appointments 
WHERE fecha_hora >= '2025-08-12' AND fecha_hora <= '2025-08-18'

ORDER BY period;