-- Script to verify the current database schema and identify missing columns

-- Check current structure of user_profiles table
SELECT 
    column_name,
    data_type,
    is_nullable,
    column_default
FROM information_schema.columns 
WHERE table_name = 'user_profiles' 
ORDER BY ordinal_position;

-- Check if Telegram columns exist
SELECT 
    CASE 
        WHEN COUNT(*) > 0 THEN 'EXISTS' 
        ELSE 'MISSING' 
    END as telegram_user_id_status,
    'telegram_user_id' as column_name
FROM information_schema.columns 
WHERE table_name = 'user_profiles' 
  AND column_name = 'telegram_user_id'

UNION ALL

SELECT 
    CASE 
        WHEN COUNT(*) > 0 THEN 'EXISTS' 
        ELSE 'MISSING' 
    END as status,
    'telegram_first_name' as column_name
FROM information_schema.columns 
WHERE table_name = 'user_profiles' 
  AND column_name = 'telegram_first_name'

UNION ALL

SELECT 
    CASE 
        WHEN COUNT(*) > 0 THEN 'EXISTS' 
        ELSE 'MISSING' 
    END as status,
    'telegram_last_name' as column_name
FROM information_schema.columns 
WHERE table_name = 'user_profiles' 
  AND column_name = 'telegram_last_name'

UNION ALL

SELECT 
    CASE 
        WHEN COUNT(*) > 0 THEN 'EXISTS' 
        ELSE 'MISSING' 
    END as status,
    'telegram_username' as column_name
FROM information_schema.columns 
WHERE table_name = 'user_profiles' 
  AND column_name = 'telegram_username'

UNION ALL

SELECT 
    CASE 
        WHEN COUNT(*) > 0 THEN 'EXISTS' 
        ELSE 'MISSING' 
    END as status,
    'telegram_language_code' as column_name
FROM information_schema.columns 
WHERE table_name = 'user_profiles' 
  AND column_name = 'telegram_language_code';

-- Also check if appointments table has the google_email column
SELECT 
    CASE 
        WHEN COUNT(*) > 0 THEN 'EXISTS' 
        ELSE 'MISSING' 
    END as google_email_status,
    'google_email' as column_name
FROM information_schema.columns 
WHERE table_name = 'appointments' 
  AND column_name = 'google_email';