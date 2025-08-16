
-- Datos de prueba para el sistema de aprendizaje
-- Ejecutar después de create_user_learning_tables.sql

-- Términos de cirugías para usuario de prueba (chat_id: 12345)
INSERT INTO user_custom_terms (chat_id, user_term, standard_term, term_type, frequency, confidence, first_seen, last_used) VALUES
(12345, 'cataratas', 'FACOEMULSIFICACION', 'surgery', 5, 0.9, NOW() - INTERVAL '7 days', NOW()),
(12345, 'faco', 'FACOEMULSIFICACION', 'surgery', 3, 0.8, NOW() - INTERVAL '5 days', NOW()),
(12345, 'quiroga', 'Dr. Andrea Quiroga', 'surgeon', 8, 0.95, NOW() - INTERVAL '10 days', NOW()),
(12345, 'magdi', 'Dra. Magdi Rodriguez', 'surgeon', 3, 0.7, NOW() - INTERVAL '3 days', NOW()),
(12345, 'callo', 'Callao', 'place', 6, 0.85, NOW() - INTERVAL '8 days', NOW()),
(12345, 'ancho', 'Sanatorio Anchorena', 'place', 4, 0.8, NOW() - INTERVAL '6 days', NOW());

-- Patrones de comunicación para el usuario
INSERT INTO user_communication_patterns (chat_id, pattern_type, pattern_value, frequency, confidence, last_used) VALUES
(12345, 'frequent_surgery', 'FACOEMULSIFICACION', 8, 0.9, NOW()),
(12345, 'typical_surgeon', 'Dr. Andrea Quiroga', 8, 0.9, NOW()),
(12345, 'preferred_place', 'Callao', 6, 0.8, NOW()),
(12345, 'usual_quantity', '1', 10, 0.9, NOW());

-- Verificar que los datos se insertaron correctamente
SELECT 'Términos personalizados:' as info;
SELECT chat_id, user_term, standard_term, term_type, frequency, confidence 
FROM user_custom_terms 
WHERE chat_id = 12345 
ORDER BY frequency DESC;

SELECT 'Patrones de comunicación:' as info;
SELECT chat_id, pattern_type, pattern_value, frequency, confidence 
FROM user_communication_patterns 
WHERE chat_id = 12345 
ORDER BY frequency DESC;
