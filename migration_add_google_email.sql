-- Migration script to add google_email column to appointments table
-- This allows team members with the same corporate email to share statistics

-- Add the google_email column (nullable initially)
ALTER TABLE appointments 
ADD COLUMN google_email TEXT;

-- Create index for performance on report queries
CREATE INDEX IF NOT EXISTS idx_appointments_google_email ON appointments(google_email);

-- Update existing records by matching chat_id with user_profiles
UPDATE appointments 
SET google_email = (
    SELECT up.google_email 
    FROM user_profiles up 
    WHERE up.chat_id = appointments.chat_id
    LIMIT 1
)
WHERE google_email IS NULL;

-- Optional: Add comment to document the purpose
COMMENT ON COLUMN appointments.google_email IS 'Google email for team-shared report statistics. Multiple users can share same email.';

-- Verify the update worked
SELECT 
    COUNT(*) as total_appointments,
    COUNT(google_email) as appointments_with_email,
    COUNT(*) - COUNT(google_email) as appointments_without_email
FROM appointments;