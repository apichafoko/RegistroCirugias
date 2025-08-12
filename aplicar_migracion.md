# Migración para corregir anestesiólogo opcional

## Problema
La columna `anestesiologo` en la tabla `appointments` está definida como `NOT NULL`, pero el código ahora permite cirugías sin anestesiólogo (valor `null`).

## Error específico
```
23502: null value in column "anestesiologo" of relation "appointments" violates not-null constraint
```

## Solución
Ejecutar la migración SQL para permitir valores `NULL` en la columna `anestesiologo`.

## Pasos para aplicar la migración

### 1. Conectarse a la base de datos
```bash
# Si usas PostgreSQL local
psql -U tu_usuario -d tu_base_de_datos

# Si usas una conexión remota
psql -h tu_host -U tu_usuario -d tu_base_de_datos
```

### 2. Ejecutar la migración
```sql
-- Cambiar la columna para permitir NULL
ALTER TABLE appointments 
ALTER COLUMN anestesiologo DROP NOT NULL;

-- Verificar el cambio
SELECT column_name, is_nullable, data_type 
FROM information_schema.columns 
WHERE table_name = 'appointments' AND column_name = 'anestesiologo';
```

### 3. Resultado esperado
La consulta de verificación debería mostrar:
```
column_name   | is_nullable | data_type
--------------+-------------+-----------
anestesiologo | YES         | text
```

## Alternativa: Ejecutar el archivo SQL
```bash
psql -U tu_usuario -d tu_base_de_datos -f fix_anestesiologo_nullable.sql
```

## Después de la migración
Una vez aplicada la migración:
1. El sistema podrá guardar cirugías sin anestesiólogo
2. El flujo "¿Querés asignar un anestesiólogo?" funcionará correctamente
3. Cuando el usuario responda "No", se guardará `NULL` en la columna `anestesiologo`
4. Los reportes mostrarán "Sin asignar" para cirugías sin anestesiólogo