"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { cloneElement, isValidElement, useEffect, useMemo, useState, type ReactNode } from "react";
import { ArrowLeft, Loader2 } from "lucide-react";
import { toast } from "sonner";
import { PageHeader } from "@/components/page-header";
import { ErrorState, ListSkeleton } from "@/components/states";
import { StatusBadge } from "@/components/status-badge";
import { useUser } from "@/components/user-context";
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert";
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from "@/components/ui/alert-dialog";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Switch } from "@/components/ui/switch";
import { Textarea } from "@/components/ui/textarea";
import { useResource } from "@/hooks/use-resource";
import { ApiError, api } from "@/lib/api";
import { TIME_ZONES, todayInZone, zoneOptions } from "@/lib/time";
import type {
  Listing,
  ListingAttributes,
  ListingInput,
  ListingStatus,
  PropertyType,
  ShowingRules,
} from "@/lib/types";
import { BlackoutDates } from "./blackout-dates";
import { PhotosEditor } from "./photos-editor";
import { SlotPreview } from "./slot-preview";
import { WeeklyGrid, validateWeekly } from "./weekly-grid";

const SLOT_LENGTHS = [15, 20, 30, 45, 60];
const BUFFERS = [0, 5, 10, 15, 30];
const PROPERTY_TYPES: PropertyType[] = ["House", "Condo", "Townhouse"];
const STATUSES: ListingStatus[] = ["Active", "Draft", "Off"];

/** Numeric inputs are kept as strings while editing so empty and partial values are allowed. */
interface DetailsForm {
  address: string;
  city: string;
  neighborhood: string;
  price: string;
  beds: string;
  baths: string;
  propertyType: PropertyType;
  hoaMonthly: string;
  floor: string;
  lotSizeSqft: string;
  description: string;
  status: ListingStatus;
  timeZone: string;
  lat: string;
  lng: string;
}

/** Rule numbers typed by the agent; kept as strings so clearing the input never yields NaN. */
interface RuleNumbers {
  leadTimeHours: string;
  horizonDays: string;
}

type FieldName = keyof DetailsForm | keyof RuleNumbers;
type FieldErrors = Partial<Record<FieldName, string>>;

/** Fields in page order, so the first error found is the first one on screen. */
const FIELD_ORDER: FieldName[] = [
  "leadTimeHours",
  "horizonDays",
  "address",
  "city",
  "neighborhood",
  "price",
  "status",
  "beds",
  "baths",
  "propertyType",
  "lotSizeSqft",
  "hoaMonthly",
  "floor",
  "description",
  "timeZone",
  "lat",
  "lng",
];

const FIELD_NAMES = new Set<string>([
  "address",
  "city",
  "neighborhood",
  "price",
  "beds",
  "baths",
  "propertyType",
  "hoaMonthly",
  "floor",
  "lotSizeSqft",
  "description",
  "status",
  "timeZone",
  "lat",
  "lng",
  "leadTimeHours",
  "horizonDays",
]);

const DEFAULT_RULES: ShowingRules = {
  slotMinutes: 30,
  bufferMinutes: 15,
  leadTimeHours: 12,
  horizonDays: 14,
  autoConfirm: false,
  weekly: [
    { dayOfWeek: "Saturday", start: "09:00", end: "12:00" },
    { dayOfWeek: "Sunday", start: "13:00", end: "16:00" },
  ],
  blackoutDates: [],
};

const str = (value: number | null | undefined) =>
  value === null || value === undefined ? "" : String(value);

/** Coordinates to 6 decimals (about 10 cm): enough for "near", and free of float noise like 39.748600006103516. */
const round6 = (value: number) => Math.round(value * 1e6) / 1e6;

function toForm(listing: Listing | null, defaultZone: string): DetailsForm {
  const a = listing?.attributes ?? {};
  return {
    address: listing?.address ?? "",
    city: listing?.city ?? "",
    neighborhood: listing?.neighborhood ?? "",
    price: str(listing?.price),
    beds: str(listing?.beds),
    baths: str(listing?.baths),
    propertyType: listing?.propertyType ?? "Condo",
    hoaMonthly: str(a.hoaMonthly as number | undefined),
    floor: str(a.floor as number | undefined),
    lotSizeSqft: str(a.lotSizeSqft as number | undefined),
    description: listing?.description ?? "",
    status: listing?.status ?? "Draft",
    timeZone: listing?.timeZone ?? defaultZone,
    lat: listing?.location ? str(round6(listing.location.lat)) : "",
    lng: listing?.location ? str(round6(listing.location.lng)) : "",
  };
}

function parseNumber(value: string): number | null {
  if (value.trim() === "") return null;
  const n = Number(value);
  return Number.isFinite(n) ? n : NaN;
}

function validate(form: DetailsForm, numbers: RuleNumbers): FieldErrors {
  const errors: FieldErrors = {};
  if (!form.address.trim()) errors.address = "Address is required.";
  if (!form.city.trim()) errors.city = "City is required.";
  const price = parseNumber(form.price);
  if (price === null || Number.isNaN(price) || price <= 0) errors.price = "Enter a price above 0.";
  const beds = parseNumber(form.beds);
  if (beds === null || Number.isNaN(beds) || beds < 0 || !Number.isInteger(beds))
    errors.beds = "Whole number, 0 or more.";
  const baths = parseNumber(form.baths);
  if (baths === null || Number.isNaN(baths) || baths < 0) errors.baths = "0 or more.";
  for (const key of ["hoaMonthly", "floor", "lotSizeSqft"] as const) {
    const n = parseNumber(form[key]);
    if (n !== null && (Number.isNaN(n) || n < 0)) errors[key] = "Must be 0 or more.";
  }
  const lat = parseNumber(form.lat);
  const lng = parseNumber(form.lng);
  if ((lat === null) !== (lng === null)) errors.lat = "Enter both latitude and longitude, or neither.";
  else if (lat !== null && (Number.isNaN(lat) || lat < -90 || lat > 90))
    errors.lat = "Latitude is -90 to 90.";
  else if (lng !== null && (Number.isNaN(lng) || lng < -180 || lng > 180))
    errors.lng = "Longitude is -180 to 180.";
  const lead = parseNumber(numbers.leadTimeHours);
  if (lead === null || !Number.isInteger(lead) || lead < 0) errors.leadTimeHours = "0 or more hours.";
  const horizon = parseNumber(numbers.horizonDays);
  if (horizon === null || !Number.isInteger(horizon) || horizon < 1 || horizon > 90)
    errors.horizonDays = "1 to 90 days.";
  return errors;
}

/** The rules as the API wants them: validated numbers merged into the structured part. */
function toRules(rules: ShowingRules, numbers: RuleNumbers): ShowingRules {
  return { ...rules, leadTimeHours: Number(numbers.leadTimeHours), horizonDays: Number(numbers.horizonDays) };
}

function toInput(
  form: DetailsForm,
  rules: ShowingRules,
  photos: string[],
  original: Listing | null,
): ListingInput {
  // Keep attributes this editor does not know about; set the per-type ones explicitly.
  const attributes: ListingAttributes = { ...(original?.attributes ?? {}) };
  delete attributes.hoaMonthly;
  delete attributes.floor;
  delete attributes.lotSizeSqft;
  if (form.propertyType === "Condo" || form.propertyType === "Townhouse") {
    attributes.hoaMonthly = parseNumber(form.hoaMonthly);
  }
  if (form.propertyType === "Condo") attributes.floor = parseNumber(form.floor);
  if (form.propertyType === "House") attributes.lotSizeSqft = parseNumber(form.lotSizeSqft);
  for (const key of Object.keys(attributes)) if (attributes[key] === null) delete attributes[key];

  const lat = parseNumber(form.lat);
  const lng = parseNumber(form.lng);
  return {
    status: form.status,
    address: form.address.trim(),
    city: form.city.trim(),
    neighborhood: form.neighborhood.trim() || null,
    price: Number(form.price),
    beds: Number(form.beds),
    baths: Number(form.baths),
    propertyType: form.propertyType,
    attributes,
    description: form.description.trim() || null,
    photos,
    timeZone: form.timeZone,
    location: lat !== null && lng !== null ? { lat: round6(lat), lng: round6(lng) } : null,
    showingRules: rules,
    // Optimistic concurrency: the API rejects the save (409) if someone else saved meanwhile.
    version: original?.version,
  };
}

/**
 * Maps ASP.NET validation keys ("Address", "showingRules.weekly[0]") to our field names. Messages
 * for anything this form does not render as a field (e.g. "weekly") are returned separately so they
 * can be shown at the top of the form instead of disappearing.
 */
function apiFieldErrors(err: ApiError): { fields: FieldErrors; other: string[] } {
  const fields: FieldErrors = {};
  const other: string[] = [];
  for (const [key, messages] of Object.entries(err.problem.errors ?? {})) {
    const field = key
      .split(".")
      .pop()!
      .replace(/\[\d+\]$/, "");
    const name = field.charAt(0).toLowerCase() + field.slice(1);
    if (FIELD_NAMES.has(name)) fields[name as FieldName] = messages[0];
    else other.push(...messages);
  }
  if (Object.keys(fields).length === 0 && other.length === 0 && err.problem.detail) {
    other.push(err.problem.detail);
  }
  return { fields, other };
}

export function ListingEditor({ id }: { id?: string }) {
  const listing = useResource(async () => (id ? api.listing(id) : null), [id]);

  if (listing.error) {
    return (
      <>
        <BackLink />
        <ErrorState
          message={
            listing.error.status === 404
              ? "This listing was not found in your brokerage."
              : listing.error.message
          }
          onRetry={listing.error.status === 404 ? undefined : listing.reload}
        />
      </>
    );
  }
  if (listing.data === undefined) {
    return (
      <>
        <BackLink />
        <ListSkeleton rows={6} />
      </>
    );
  }
  // Keyed on the version too, so a reload after a stale-save conflict starts the form from fresh data.
  return (
    <EditorForm
      key={listing.data ? `${listing.data.id}:${listing.data.version ?? 0}` : "new"}
      original={listing.data}
      onReload={listing.reload}
    />
  );
}

function BackLink() {
  return (
    <Button asChild variant="ghost" size="sm" className="mb-4 -ml-2">
      <Link href="/agent/listings">
        <ArrowLeft />
        My listings
      </Link>
    </Button>
  );
}

function EditorForm({ original, onReload }: { original: Listing | null; onReload: () => void }) {
  const user = useUser();
  const router = useRouter();
  const defaultZone = (TIME_ZONES as readonly string[]).includes(user.timeZone)
    ? user.timeZone
    : "America/Denver";
  const [saved, setSaved] = useState<Listing | null>(original);
  const [form, setForm] = useState<DetailsForm>(() => toForm(original, defaultZone));
  const [rules, setRules] = useState<ShowingRules>(() => original?.showingRules ?? DEFAULT_RULES);
  const [numbers, setNumbers] = useState<RuleNumbers>(() => {
    const r = original?.showingRules ?? DEFAULT_RULES;
    return { leadTimeHours: str(r.leadTimeHours), horizonDays: str(r.horizonDays) };
  });
  const [photos, setPhotos] = useState<string[]>(() => original?.photos ?? []);
  const [errors, setErrors] = useState<FieldErrors>({});
  const [formError, setFormError] = useState<string[]>([]);
  const [dirty, setDirty] = useState(false);
  const [saving, setSaving] = useState(false);
  const [previewVersion, setPreviewVersion] = useState(0);

  const weeklyErrors = useMemo(() => validateWeekly(rules.weekly), [rules.weekly]);
  const hasWeeklyErrors = Object.keys(weeklyErrors).length > 0;

  function setField<K extends keyof DetailsForm>(key: K, value: DetailsForm[K]) {
    setForm((f) => ({ ...f, [key]: value }));
    setErrors((e) => ({ ...e, [key]: undefined }));
    setDirty(true);
  }

  function setRule<K extends keyof ShowingRules>(key: K, value: ShowingRules[K]) {
    setRules((r) => ({ ...r, [key]: value }));
    setDirty(true);
  }

  function setNumber(key: keyof RuleNumbers, value: string) {
    setNumbers((n) => ({ ...n, [key]: value }));
    setErrors((e) => ({ ...e, [key]: undefined }));
    setDirty(true);
  }

  async function save() {
    const fieldErrors = validate(form, numbers);
    setErrors(fieldErrors);
    setFormError([]);
    if (Object.keys(fieldErrors).length > 0 || hasWeeklyErrors) {
      toast.error("Fix the highlighted fields before saving.");
      // Take the user to the first problem instead of leaving the click a silent no-op.
      const firstField = FIELD_ORDER.find((name) => fieldErrors[name]);
      const target = firstField ? document.getElementById(firstField) : document.getElementById("weekly-grid");
      target?.scrollIntoView({ behavior: "smooth", block: "center" });
      if (target instanceof HTMLElement) target.focus({ preventScroll: true });
      return;
    }
    setSaving(true);
    const nextRules = toRules(rules, numbers);
    const input = toInput(form, nextRules, photos, saved);
    try {
      // One request either way: the rules travel in the body and the API applies them, so the
      // listing's version moves by exactly one per save and `saved` always carries the current one
      // (a second PUT to the rules endpoint would bump it again and make the next save a 409).
      const result = saved ? await api.updateListing(saved.id, input) : await api.createListing(input);
      setSaved({ ...result, showingRules: result.showingRules ?? nextRules });
      setDirty(false);
      setPreviewVersion((v) => v + 1);
      toast.success(saved ? "Listing saved" : "Listing created");
      if (!saved) router.replace(`/agent/listings/${result.id}`);
    } catch (err) {
      if (err instanceof ApiError && err.status === 409 && saved) {
        // Someone else saved this listing since it was loaded; the user has to start from their copy.
        toast.error(err.message, {
          duration: 10_000,
          action: { label: "Reload", onClick: onReload },
        });
      } else if (err instanceof ApiError) {
        const { fields, other } = apiFieldErrors(err);
        setErrors(fields);
        setFormError(other);
        toast.error(err.message);
      } else {
        toast.error("Could not save the listing.");
      }
    } finally {
      setSaving(false);
    }
  }

  const zones = zoneOptions(form.timeZone);

  const rulesCard = (
    <Card>
      <CardHeader>
        <CardTitle>Showing rules</CardTitle>
        <CardDescription>
          Weekly windows in the listing&apos;s time zone ({form.timeZone}). Slots are cut from each window,
          with a buffer between showings.
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-6">
        <WeeklyGrid
          weekly={rules.weekly}
          onChange={(weekly) => setRule("weekly", weekly)}
          errors={weeklyErrors}
        />

        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
          <Field label="Slot length" htmlFor="slotMinutes">
            <Select
              value={String(rules.slotMinutes)}
              onValueChange={(v) => setRule("slotMinutes", Number(v))}
            >
              <SelectTrigger id="slotMinutes" className="w-full">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {SLOT_LENGTHS.map((m) => (
                  <SelectItem key={m} value={String(m)}>
                    {m} minutes
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </Field>
          <Field label="Buffer between" htmlFor="bufferMinutes">
            <Select
              value={String(rules.bufferMinutes)}
              onValueChange={(v) => setRule("bufferMinutes", Number(v))}
            >
              <SelectTrigger id="bufferMinutes" className="w-full">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {BUFFERS.map((m) => (
                  <SelectItem key={m} value={String(m)}>
                    {m === 0 ? "No buffer" : `${m} minutes`}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </Field>
          <Field label="Lead time (hours)" htmlFor="leadTimeHours" error={errors.leadTimeHours}>
            <Input
              id="leadTimeHours"
              type="number"
              min={0}
              step={1}
              inputMode="numeric"
              value={numbers.leadTimeHours}
              onChange={(e) => setNumber("leadTimeHours", e.target.value)}
            />
          </Field>
          <Field label="Book ahead (days)" htmlFor="horizonDays" error={errors.horizonDays}>
            <Input
              id="horizonDays"
              type="number"
              min={1}
              max={90}
              step={1}
              inputMode="numeric"
              value={numbers.horizonDays}
              onChange={(e) => setNumber("horizonDays", e.target.value)}
            />
          </Field>
        </div>

        <div className="flex items-start justify-between gap-4 rounded-lg border p-3">
          <div className="space-y-0.5">
            <Label htmlFor="autoConfirm">Auto-confirm requests</Label>
            <p className="text-sm text-muted-foreground">
              Bookings are confirmed instantly instead of waiting for your approval.
            </p>
          </div>
          <Switch
            id="autoConfirm"
            checked={rules.autoConfirm}
            onCheckedChange={(checked) => setRule("autoConfirm", checked)}
          />
        </div>

        <div className="space-y-2">
          <Label>Blackout dates</Label>
          <p className="text-sm text-muted-foreground">No showings on these days (listing time).</p>
          <BlackoutDates
            dates={rules.blackoutDates}
            onChange={(dates) => setRule("blackoutDates", dates)}
            minDate={todayInZone(form.timeZone)}
          />
        </div>
      </CardContent>
    </Card>
  );

  const detailsCards = (
    <>
      <Card>
        <CardHeader>
          <CardTitle>Details</CardTitle>
        </CardHeader>
        <CardContent className="grid gap-4 sm:grid-cols-2">
          <Field label="Address" htmlFor="address" error={errors.address} className="sm:col-span-2">
            <Input id="address" value={form.address} onChange={(e) => setField("address", e.target.value)} />
          </Field>
          <Field label="City" htmlFor="city" error={errors.city}>
            <Input id="city" value={form.city} onChange={(e) => setField("city", e.target.value)} />
          </Field>
          <Field label="Neighborhood" htmlFor="neighborhood" error={errors.neighborhood}>
            <Input
              id="neighborhood"
              value={form.neighborhood}
              onChange={(e) => setField("neighborhood", e.target.value)}
            />
          </Field>
          <Field label="Price" htmlFor="price" error={errors.price}>
            <Input
              id="price"
              type="number"
              min={0}
              step={1000}
              inputMode="numeric"
              value={form.price}
              onChange={(e) => setField("price", e.target.value)}
            />
          </Field>
          <Field label="Status" htmlFor="status">
            <Select value={form.status} onValueChange={(v) => setField("status", v as ListingStatus)}>
              <SelectTrigger id="status" className="w-full">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {STATUSES.map((s) => (
                  <SelectItem key={s} value={s}>
                    {s === "Active" ? "Active (bookable)" : s === "Draft" ? "Draft (hidden)" : "Off market"}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </Field>
          <Field label="Beds" htmlFor="beds" error={errors.beds}>
            <Input
              id="beds"
              type="number"
              min={0}
              step={1}
              value={form.beds}
              onChange={(e) => setField("beds", e.target.value)}
            />
          </Field>
          <Field label="Baths" htmlFor="baths" error={errors.baths}>
            <Input
              id="baths"
              type="number"
              min={0}
              step={0.5}
              value={form.baths}
              onChange={(e) => setField("baths", e.target.value)}
            />
          </Field>
          <Field label="Property type" htmlFor="propertyType">
            <Select
              value={form.propertyType}
              onValueChange={(v) => setField("propertyType", v as PropertyType)}
            >
              <SelectTrigger id="propertyType" className="w-full">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {PROPERTY_TYPES.map((t) => (
                  <SelectItem key={t} value={t}>
                    {t}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </Field>
          {form.propertyType === "House" ? (
            <Field label="Lot size (sq ft)" htmlFor="lotSizeSqft" error={errors.lotSizeSqft}>
              <Input
                id="lotSizeSqft"
                type="number"
                min={0}
                value={form.lotSizeSqft}
                onChange={(e) => setField("lotSizeSqft", e.target.value)}
              />
            </Field>
          ) : (
            <Field label="HOA per month" htmlFor="hoaMonthly" error={errors.hoaMonthly}>
              <Input
                id="hoaMonthly"
                type="number"
                min={0}
                value={form.hoaMonthly}
                onChange={(e) => setField("hoaMonthly", e.target.value)}
              />
            </Field>
          )}
          {form.propertyType === "Condo" ? (
            <Field label="Floor" htmlFor="floor" error={errors.floor}>
              <Input
                id="floor"
                type="number"
                min={0}
                step={1}
                value={form.floor}
                onChange={(e) => setField("floor", e.target.value)}
              />
            </Field>
          ) : null}
          <Field label="Description" htmlFor="description" className="sm:col-span-2">
            <Textarea
              id="description"
              rows={4}
              value={form.description}
              onChange={(e) => setField("description", e.target.value)}
            />
          </Field>
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>Photos</CardTitle>
          <CardDescription>Paste image URLs. The first photo is the cover on every card.</CardDescription>
        </CardHeader>
        <CardContent>
          <PhotosEditor
            photos={photos}
            label={form.propertyType}
            onChange={(next) => {
              setPhotos(next);
              setDirty(true);
            }}
          />
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>Location and time zone</CardTitle>
          <CardDescription>
            Coordinates power &quot;near&quot; search. The time zone anchors every showing window.
          </CardDescription>
        </CardHeader>
        <CardContent className="grid gap-4 sm:grid-cols-3">
          <Field label="Time zone" htmlFor="timeZone">
            <Select value={form.timeZone} onValueChange={(v) => setField("timeZone", v)}>
              <SelectTrigger id="timeZone" className="w-full">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {zones.map((z) => (
                  <SelectItem key={z} value={z}>
                    {z}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </Field>
          <Field label="Latitude" htmlFor="lat" error={errors.lat}>
            <Input
              id="lat"
              type="number"
              step="any"
              placeholder="39.7508"
              value={form.lat}
              onChange={(e) => setField("lat", e.target.value)}
            />
          </Field>
          <Field label="Longitude" htmlFor="lng" error={errors.lng}>
            <Input
              id="lng"
              type="number"
              step="any"
              placeholder="-104.9997"
              value={form.lng}
              onChange={(e) => setField("lng", e.target.value)}
            />
          </Field>
        </CardContent>
      </Card>
    </>
  );

  const unsavedGuard = useUnsavedChangesGuard(dirty && !saving);

  return (
    <>
      <BackLink />
      {unsavedGuard}
      <PageHeader
        title={saved ? saved.address : "New listing"}
        description={
          saved ? (
            <span className="inline-flex items-center gap-2">
              <StatusBadge status={saved.status} /> Showing windows are in {saved.timeZone} time.
            </span>
          ) : (
            "Add the home, then set when it can be shown."
          )
        }
        actions={
          <>
            {dirty ? (
              <span className="text-sm text-muted-foreground">
                {hasWeeklyErrors ? "Fix the showing windows to save" : "Unsaved changes"}
              </span>
            ) : null}
            <Button
              onClick={save}
              disabled={saving || hasWeeklyErrors || (!dirty && saved !== null)}
              title={hasWeeklyErrors ? "A showing window is invalid." : undefined}
            >
              {saving ? <Loader2 className="animate-spin" /> : null}
              {saved ? "Save changes" : "Create listing"}
            </Button>
          </>
        }
      />

      <div className="grid gap-6 lg:grid-cols-[minmax(0,1fr)_340px]">
        <div className="space-y-6">
          {formError.length > 0 ? (
            <Alert variant="destructive" role="alert">
              <AlertTitle>The listing could not be saved</AlertTitle>
              <AlertDescription>
                <ul className={formError.length > 1 ? "list-disc pl-4" : undefined}>
                  {formError.map((message) => (
                    <li key={message}>{message}</li>
                  ))}
                </ul>
              </AlertDescription>
            </Alert>
          ) : null}
          {saved ? (
            <>
              {rulesCard}
              {detailsCards}
            </>
          ) : (
            <>
              {detailsCards}
              {rulesCard}
            </>
          )}
        </div>

        <div className="lg:sticky lg:top-20 lg:self-start">
          <SlotPreview
            listingId={saved?.id ?? null}
            timeZone={saved?.timeZone ?? form.timeZone}
            version={previewVersion}
          />
        </div>
      </div>
    </>
  );
}

/**
 * Guards a dirty form: the browser's own prompt for reloads and closed tabs (`beforeunload`), and
 * a "Discard changes?" dialog for in-app links, which the App Router cannot block itself. Links
 * are caught at the document level in the capture phase, before Next's own click handler runs.
 */
function useUnsavedChangesGuard(dirty: boolean): ReactNode {
  const router = useRouter();
  const [pendingHref, setPendingHref] = useState<string | null>(null);

  useEffect(() => {
    if (!dirty) return;
    const onBeforeUnload = (event: BeforeUnloadEvent) => {
      event.preventDefault();
    };
    const onClick = (event: MouseEvent) => {
      if (event.defaultPrevented || event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey) return;
      const anchor = (event.target as Element | null)?.closest("a[href]");
      if (!(anchor instanceof HTMLAnchorElement) || anchor.target === "_blank" || anchor.hasAttribute("download"))
        return;
      const url = new URL(anchor.href, window.location.href);
      if (url.origin !== window.location.origin) return; // the browser prompts for these itself
      if (url.pathname === window.location.pathname && url.search === window.location.search) return;
      event.preventDefault();
      event.stopPropagation();
      setPendingHref(url.pathname + url.search + url.hash);
    };
    window.addEventListener("beforeunload", onBeforeUnload);
    document.addEventListener("click", onClick, true);
    return () => {
      window.removeEventListener("beforeunload", onBeforeUnload);
      document.removeEventListener("click", onClick, true);
    };
  }, [dirty]);

  return (
    <AlertDialog open={pendingHref !== null} onOpenChange={(open) => (!open ? setPendingHref(null) : undefined)}>
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>Discard changes?</AlertDialogTitle>
          <AlertDialogDescription>
            This listing has unsaved changes. Leaving the page throws them away.
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel>Keep editing</AlertDialogCancel>
          <AlertDialogAction
            onClick={() => {
              const href = pendingHref;
              setPendingHref(null);
              if (href) router.push(href);
            }}
          >
            Discard
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  );
}

function Field({
  label,
  htmlFor,
  error,
  className,
  children,
}: {
  label: string;
  htmlFor: string;
  error?: string;
  className?: string;
  children: ReactNode;
}) {
  const errorId = `${htmlFor}-error`;
  // Point assistive tech at the message: the control is the single child (an Input or a Select root).
  const control =
    error && isValidElement<Record<string, unknown>>(children)
      ? cloneElement(children, { "aria-invalid": true, "aria-describedby": errorId })
      : children;
  return (
    <div className={`space-y-2 ${className ?? ""}`}>
      <Label htmlFor={htmlFor}>{label}</Label>
      {control}
      {error ? (
        <p id={errorId} className="text-xs text-destructive">
          {error}
        </p>
      ) : null}
    </div>
  );
}
