-- Restaurar foreign keys que permiten chat_id nulo en user_profiles
-- Estas FK solo validan cuando chat_id NO es nulo

BEGIN;

-- Primero crear un constraint único que permita NULLs múltiples
-- pero sea único para valores no-nulos
ALTER TABLE user_profiles 
ADD CONSTRAINT user_profiles_chat_id_unique 
UNIQUE (chat_id);

-- FK para appointments
ALTER TABLE appointments
ADD CONSTRAINT appointments_chat_id_fkey 
FOREIGN KEY (chat_id) 
REFERENCES user_profiles(chat_id)
ON DELETE SET NULL;

-- FK para surgery_events  
ALTER TABLE surgery_events
ADD CONSTRAINT surgery_events_chat_id_fkey 
FOREIGN KEY (chat_id) 
REFERENCES user_profiles(chat_id)
ON DELETE CASCADE;

COMMIT;

-- NOTA: Estas FK funcionarán así:
-- - Si chat_id es NULL en user_profiles, no afecta las validaciones
-- - Si borras un user_profile, appointments.chat_id se pone NULL
-- - Si borras un user_profile, surgery_events se eliminan (CASCADE)
-- - appointments/surgery_events solo pueden tener chat_id que existan en user_profiles