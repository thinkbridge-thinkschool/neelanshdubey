/** Mirrors Quote.ValidateFields in the real API (Models/Quote.cs) so invalid requests are caught client-side. */
export function validateQuoteFields(author: string, text: string): string[] {
  const errors: string[] = [];
  const trimmedAuthor = author.trim();
  const trimmedText = text.trim();

  if (!trimmedAuthor) errors.push('Author is required.');
  else if (trimmedAuthor.length > 200) errors.push('Author must be 200 characters or fewer.');

  if (!trimmedText) errors.push('Quote text is required.');
  else if (trimmedText.length > 1000) errors.push('Quote text must be 1000 characters or fewer.');

  return errors;
}
