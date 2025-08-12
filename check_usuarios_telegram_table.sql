-- Script to check if usuarios_telegram table exists and get its structure

-- Check if usuarios_telegram table exists
SELECT 
    CASE 
        WHEN COUNT(*) > 0 THEN 'EXISTS' 
        ELSE 'MISSING' 
    END as table_status
FROM information_schema.tables 
WHERE table_name = 'usuarios_telegram';

-- If it exists, show its structure
SELECT 
    column_name,
    data_type,
    is_nullable,
    column_default,
    character_maximum_length
FROM information_schema.columns 
WHERE table_name = 'usuarios_telegram' 
ORDER BY ordinal_position;

-- Show indexes on the table
SELECT 
    indexname,
    indexdef
FROM pg_indexes 
WHERE tablename = 'usuarios_telegram';

-- Show sample data (first 5 rows)
-- Uncomment the next line if the table exists
-- SELECT * FROM usuarios_telegram LIMIT 5;