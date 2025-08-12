-- Migration script to add Telegram-specific fields to user_profiles table
-- These fields store additional information from Telegram users

-- Add Telegram fields to user_profiles table
ALTER TABLE user_profiles 
ADD COLUMN IF NOT EXISTS telegram_user_id BIGINT NULL;

ALTER TABLE user_profiles 
ADD COLUMN IF NOT EXISTS telegram_first_name TEXT NULL;

ALTER TABLE user_profiles 
ADD COLUMN IF NOT EXISTS telegram_last_name TEXT NULL;

ALTER TABLE user_profiles 
ADD COLUMN IF NOT EXISTS telegram_username TEXT NULL;

ALTER TABLE user_profiles 
ADD COLUMN IF NOT EXISTS telegram_language_code TEXT NULL;

-- Create index on telegram_user_id for performance (optional but recommended)
CREATE INDEX IF NOT EXISTS idx_user_profiles_telegram_user_id ON user_profiles(telegram_user_id);

-- Add comments to document the new fields
COMMENT ON COLUMN user_profiles.telegram_user_id IS 'Telegram unique user identifier';
COMMENT ON COLUMN user_profiles.telegram_first_name IS 'User first name from Telegram profile';
COMMENT ON COLUMN user_profiles.telegram_last_name IS 'User last name from Telegram profile';
COMMENT ON COLUMN user_profiles.telegram_username IS 'Telegram username (without @)';
COMMENT ON COLUMN user_profiles.telegram_language_code IS 'User language code from Telegram (e.g., en, es)';

-- Verify the changes
\d user_profiles;

-- Show a sample of the table structure
SELECT column_name, data_type, is_nullable, column_default
FROM information_schema.columns 
WHERE table_name = 'user_profiles' 
  AND column_name LIKE 'telegram_%'
ORDER BY column_name;