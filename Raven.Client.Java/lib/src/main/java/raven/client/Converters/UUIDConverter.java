package raven.client.Converters;

import java.util.UUID;

import raven.abstractions.data.Constants;

/**
 * Convert string from / to UUID
 */
public class UUIDConverter implements ITypeConverter {

  @Override
  public boolean canConvertFrom(Class< ? > sourceType) {
    return UUID.class.equals(sourceType);
  }

  @Override
  public String convertFrom(String tag, Object value, boolean allowNull) {
    UUID val = (UUID) value;
    if (Constants.EMPTY_UUID.equals(val)) {
      return tag + UUID.randomUUID();
    }
    return tag + value;
  }

  @Override
  public Object convertTo(String value) {
    return UUID.fromString(value);
  }

}
